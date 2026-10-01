using System.Runtime.InteropServices;
using SocAutoTask.Input;
using SocAutoTask.Model;
using SocAutoTask.Playback;

namespace SocAutoTask.Native;

/// <summary>
/// Envia los eventos con SendInput (ARQUITECTURA §7): raton en coordenadas absolutas del
/// escritorio virtual, botones con su movimiento en la misma entrada, teclas con vk + scan.
/// </summary>
public sealed class Win32InputSink : IInputSink
{
    /// <summary>Marca en dwExtraInfo de lo que envia AutoTask (por si alguien quiere distinguirlo).</summary>
    public const nint Signature = 0x50C7A5C;

    private ScreenRect _screen = Screens.Virtual();
    private long _lastScreenCheck = Environment.TickCount64;

    /// <summary>Eventos que Windows no acepto (SendInput devolvio 0: escritorio bloqueado, UIPI...).</summary>
    public int Rejected { get; private set; }

    public unsafe void Send(in MacroEvent e)
    {
        // La pantalla puede cambiar mientras se reproduce (un monitor que se apaga): se mira cada segundo.
        if (Environment.TickCount64 - _lastScreenCheck > 1000)
        {
            _screen = Screens.Virtual();
            _lastScreenCheck = Environment.TickCount64;
        }

        Win32.INPUT input = default;
        switch (e.Kind)
        {
            case EventKind.MouseMove or EventKind.MouseDown or EventKind.MouseUp or EventKind.Wheel or EventKind.HWheel:
                var (x, y) = _screen.Clamp(e.X, e.Y);
                input.type = Win32.INPUT_MOUSE;
                input.u.mi.dx = CoordinateMapper.Normalize(x, _screen.Left, _screen.Width);
                input.u.mi.dy = CoordinateMapper.Normalize(y, _screen.Top, _screen.Height);
                input.u.mi.dwFlags = Win32.MOUSEEVENTF_MOVE | Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK | ButtonFlags(e, out var data);
                input.u.mi.mouseData = data;
                input.u.mi.dwExtraInfo = Signature;
                break;
            case EventKind.KeyDown or EventKind.KeyUp:
                var scan = e.ScanCode != 0 ? e.ScanCode : (ushort)Win32.MapVirtualKey(e.VirtualKey, 0);
                input.type = Win32.INPUT_KEYBOARD;
                input.u.ki.wVk = e.VirtualKey;
                input.u.ki.wScan = scan;
                input.u.ki.dwFlags = (e.IsExtended ? Win32.KEYEVENTF_EXTENDEDKEY : 0) | (e.Kind == EventKind.KeyUp ? Win32.KEYEVENTF_KEYUP : 0);
                input.u.ki.dwExtraInfo = Signature;
                break;
            default:
                return;
        }
        if (Win32.SendInput(1, &input, Marshal.SizeOf<Win32.INPUT>()) == 0)
            Rejected++;
    }

    private static uint ButtonFlags(in MacroEvent e, out uint data)
    {
        data = 0;
        switch (e.Kind)
        {
            case EventKind.Wheel:
                data = unchecked((uint)e.Data);
                return Win32.MOUSEEVENTF_WHEEL;
            case EventKind.HWheel:
                data = unchecked((uint)e.Data);
                return Win32.MOUSEEVENTF_HWHEEL;
            case EventKind.MouseDown or EventKind.MouseUp:
                var down = e.Kind == EventKind.MouseDown;
                switch (e.Button)
                {
                    case MouseButton.Left: return down ? Win32.MOUSEEVENTF_LEFTDOWN : Win32.MOUSEEVENTF_LEFTUP;
                    case MouseButton.Right: return down ? Win32.MOUSEEVENTF_RIGHTDOWN : Win32.MOUSEEVENTF_RIGHTUP;
                    case MouseButton.Middle: return down ? Win32.MOUSEEVENTF_MIDDLEDOWN : Win32.MOUSEEVENTF_MIDDLEUP;
                    default:
                        data = e.Button == MouseButton.X2 ? 2u : 1u;
                        return down ? Win32.MOUSEEVENTF_XDOWN : Win32.MOUSEEVENTF_XUP;
                }
            default:
                return 0;
        }
    }
}

/// <summary>Modificadores pulsados de verdad ahora mismo (RF-15).</summary>
public sealed class SystemKeyboardState : IKeyboardState
{
    public bool AnyModifierDown() =>
        IsDown(KeyNames.Shift) || IsDown(KeyNames.Control) || IsDown(KeyNames.Menu) || IsDown(KeyNames.LWin) || IsDown(KeyNames.RWin);

    private static bool IsDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;
}

/// <summary>
/// Vigila la parada de emergencia mientras se reproduce (RF-13): gancho LL de teclado en su hilo
/// y un temporizador para la Esc mantenida. Llama a <c>onStop</c> una sola vez.
/// </summary>
public sealed class EmergencyStopWatcher(EmergencyKeys keys, int escapeHoldMs, Action onStop) : HookThread(mouse: false, keyboard: true, timerMs: 50)
{
    private readonly EmergencyStopLogic _logic = new(keys, escapeHoldMs);
    private int _fired;

    private protected override void OnKeyboard(int message, in Win32.KBDLLHOOKSTRUCT data)
    {
        var down = message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
        var injected = (data.flags & (Win32.LLKHF_INJECTED | Win32.LLKHF_LOWER_IL_INJECTED)) != 0;
        if (_logic.OnKey((ushort)data.vkCode, down, injected, Environment.TickCount64))
            Fire();
    }

    protected override void OnTimer()
    {
        if (_logic.OnTick(Environment.TickCount64))
            Fire();
    }

    private void Fire()
    {
        if (Interlocked.Exchange(ref _fired, 1) == 0)
            ThreadPool.QueueUserWorkItem(_ => onStop());
    }
}

/// <summary>¿Esta elevado este proceso o el de una ventana? (RF-18: UIPI descarta lo enviado a una ventana elevada.)</summary>
public static class Elevation
{
    private const uint ProcessQueryLimitedInformation = 0x1000, TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;

    public static bool IsCurrentProcessElevated() => IsElevated(Win32.GetCurrentProcess(), closeProcess: false) == true;

    /// <summary>Verdadero si la ventana es de un proceso elevado (o no se le puede preguntar, que suele ser lo mismo).</summary>
    public static bool IsWindowElevated(nint hwnd)
    {
        if (hwnd == 0)
            return false;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
            return false;
        var process = Win32.OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
            return true;
        return IsElevated(process, closeProcess: true) ?? true;
    }

    public static bool ForegroundIsElevatedAndWeAreNot() =>
        !IsCurrentProcessElevated() && IsWindowElevated(Win32.GetForegroundWindow());

    private static unsafe bool? IsElevated(nint process, bool closeProcess)
    {
        try
        {
            if (!Win32.OpenProcessToken(process, TokenQuery, out var token))
                return null;
            try
            {
                int elevation = 0;
                return Win32.GetTokenInformation(token, TokenElevationClass, &elevation, sizeof(int), out _) ? elevation != 0 : null;
            }
            finally
            {
                Win32.CloseHandle(token);
            }
        }
        finally
        {
            if (closeProcess)
                Win32.CloseHandle(process);
        }
    }
}

/// <summary>Aviso nativo (para el reproductor, que no tiene ventana).</summary>
public static class NativeDialogs
{
    private const uint MbError = 0x10 | 0x40000 | 0x10000;   // MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND

    public static void Error(string text, string caption) => Win32.MessageBox(0, text, caption, MbError);
}
