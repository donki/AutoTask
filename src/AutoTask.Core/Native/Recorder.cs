using SocAutoTask.Editing;
using SocAutoTask.Model;
using SocAutoTask.Playback;

namespace SocAutoTask.Native;

/// <summary>Que se graba (RF-06, RF-07).</summary>
public sealed record RecorderOptions
{
    public bool Keyboard { get; init; } = true;
    public bool MouseMoves { get; init; } = true;

    /// <summary>
    /// Solo para la prueba E2E: acepta eventos inyectados (los de SendInput). En uso normal se
    /// ignoran siempre, tambien los de la propia reproduccion.
    /// </summary>
    public bool IncludeInjected { get; init; }
}

/// <summary>
/// Graba raton y teclado de todo el sistema (RF-01..07) con ganchos LL en su hilo. Los tiempos
/// salen del propio evento (milisegundos del sistema), no de cuando se procesa.
/// </summary>
public sealed class Recorder(RecorderOptions options) : HookThread(mouse: true, keyboard: options.Keyboard), IRecorder
{
    private readonly object _gate = new();
    private readonly List<MacroEvent> _events = new(4096);
    private uint? _lastTime;
    private volatile int _count;
    private volatile IgnoreArea? _ignore;

    /// <summary>Cuantos eventos lleva (se puede leer desde la interfaz).</summary>
    public int Count => _count;

    /// <summary>Pantalla virtual cuando empezo a grabar.</summary>
    public ScreenRect Screen { get; private set; }

    /// <summary>
    /// Rectangulo donde los clics y la rueda no se graban: la ventana de AutoTask (RF-05). La
    /// interfaz lo pone al día al mover la ventana; el gancho lo lee sin bloquear.
    /// </summary>
    public void SetIgnoredArea(ScreenRect? area) => _ignore = area is { } a ? new IgnoreArea(a) : null;

    public new void Start()
    {
        Screen = Screens.Virtual();
        base.Start();
    }

    /// <summary>Para y devuelve la grabacion ya limpia (sin el atajo ni sueltas huerfanas).</summary>
    public Recording StopAndCollect(PlaybackOptions playback)
    {
        Stop();
        List<MacroEvent> raw;
        lock (_gate)
            raw = [.. _events];
        return new Recording(RecordingCleaner.Clean(raw), playback, Screen);
    }

    private protected override void OnMouse(int message, in Win32.MSLLHOOKSTRUCT data)
    {
        if (!options.IncludeInjected && (data.flags & (Win32.LLMHF_INJECTED | Win32.LLMHF_LOWER_IL_INJECTED)) != 0)
            return;
        int x = data.pt.X, y = data.pt.Y;
        MacroEvent e;
        switch (message)
        {
            case Win32.WM_MOUSEMOVE:
                if (!options.MouseMoves)
                    return;
                e = MacroEvent.Move(x, y);
                break;
            case Win32.WM_LBUTTONDOWN: e = MacroEvent.Down(MouseButton.Left, x, y); break;
            case Win32.WM_LBUTTONUP: e = MacroEvent.Up(MouseButton.Left, x, y); break;
            case Win32.WM_RBUTTONDOWN: e = MacroEvent.Down(MouseButton.Right, x, y); break;
            case Win32.WM_RBUTTONUP: e = MacroEvent.Up(MouseButton.Right, x, y); break;
            case Win32.WM_MBUTTONDOWN: e = MacroEvent.Down(MouseButton.Middle, x, y); break;
            case Win32.WM_MBUTTONUP: e = MacroEvent.Up(MouseButton.Middle, x, y); break;
            case Win32.WM_XBUTTONDOWN or Win32.WM_XBUTTONUP:
                var button = (data.mouseData >> 16) == 2 ? MouseButton.X2 : MouseButton.X1;
                e = message == Win32.WM_XBUTTONDOWN ? MacroEvent.Down(button, x, y) : MacroEvent.Up(button, x, y);
                break;
            case Win32.WM_MOUSEWHEEL or Win32.WM_MOUSEHWHEEL:
                e = MacroEvent.WheelAt(x, y, (short)(data.mouseData >> 16), horizontal: message == Win32.WM_MOUSEHWHEEL);
                break;
            default:
                return;
        }
        if (e.Kind != EventKind.MouseMove && _ignore is { } area && area.Rect.Contains(x, y))
            return;
        Add(e, data.time);
    }

    private protected override void OnKeyboard(int message, in Win32.KBDLLHOOKSTRUCT data)
    {
        if (!options.IncludeInjected && (data.flags & (Win32.LLKHF_INJECTED | Win32.LLKHF_LOWER_IL_INJECTED)) != 0)
            return;
        var down = message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
        var up = message is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
        if (!down && !up)
            return;
        Add(MacroEvent.Key(down, (ushort)data.vkCode, (ushort)data.scanCode, (data.flags & Win32.LLKHF_EXTENDED) != 0), data.time);
    }

    private void Add(MacroEvent e, uint time)
    {
        lock (_gate)
        {
            var delay = _lastTime is { } last ? unchecked(time - last) : 0u;
            if (delay > int.MaxValue)
                delay = 0;   // el reloj del evento no va hacia atras; si lo parece, sin espera
            _lastTime = time;
            _events.Add(e with { DelayMs = (int)delay });
            _count = _events.Count;
        }
    }

    private sealed class IgnoreArea(ScreenRect rect)
    {
        public ScreenRect Rect { get; } = rect;
    }
}

/// <summary>Pantallas (en pixeles fisicos: el proceso es per-monitor DPI aware).</summary>
public static class Screens
{
    public static ScreenRect Virtual() => new(
        Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));

    public static (int X, int Y) Cursor() => Win32.GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    public static void SetCursor(int x, int y) => Win32.SetCursorPos(x, y);
}
