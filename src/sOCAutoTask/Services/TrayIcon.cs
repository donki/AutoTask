using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SocAutoTask.Desktop.Services;

/// <summary>
/// Icono en el area de notificacion (RF-43), como el de RC Manager: al minimizar la ventana se
/// esconde y queda el icono; clic para volver, boton derecho para Abrir, Grabar/Parar,
/// Reproducir/Parar o Salir. Shell_NotifyIcon y un gancho en el procedimiento de la ventana.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WmSysCommand = 0x0112, WmCommand = 0x0111, WmRButtonUp = 0x0205, WmLButtonUp = 0x0202, WmLButtonDblClk = 0x0203;
    private const int WmTray = 0x8001;
    private const int ScMinimize = 0xF020;
    private const int IdOpen = 1, IdRecord = 2, IdPlay = 3, IdExit = 4;

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly Func<string, string> _text;
    private readonly Action _record, _play, _exit;
    private bool _shown;

    public bool MinimizeToTray { get; set; } = true;

    public TrayIcon(Window window, Func<string, string> text, Action record, Action play, Action exit)
    {
        _window = window;
        _text = text;
        _record = record;
        _play = play;
        _exit = exit;
        _hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized && MinimizeToTray)
                window.Dispatcher.BeginInvoke(() => { window.WindowState = WindowState.Normal; HideToTray(); });
        };
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmSysCommand when ((long)wParam & 0xFFF0) == ScMinimize && MinimizeToTray:
                HideToTray();
                handled = true;
                break;
            case WmTray:
                var evt = (int)((long)lParam & 0xFFFF);
                if (evt is WmLButtonUp or WmLButtonDblClk) Restore();
                else if (evt == WmRButtonUp) ShowMenu();
                handled = true;
                break;
            case WmCommand:
                switch ((int)((long)wParam & 0xFFFF))
                {
                    case IdOpen: Restore(); handled = true; break;
                    case IdRecord: _record(); handled = true; break;
                    case IdPlay: _play(); handled = true; break;
                    case IdExit: Remove(); _exit(); handled = true; break;
                }
                break;
        }
        return IntPtr.Zero;
    }

    public void HideToTray()
    {
        Add();
        _window.Hide();
    }

    public void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        SetForegroundWindow(_hwnd);
        Remove();
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, IdOpen, _text("TrayOpen"));
        AppendMenu(menu, 0, IdRecord, _text("TrayRecord"));
        AppendMenu(menu, 0, IdPlay, _text("TrayPlay"));
        AppendMenu(menu, 0x800, 0, null);
        AppendMenu(menu, 0, IdExit, _text("TrayExit"));
        GetCursorPos(out var p);
        SetForegroundWindow(_hwnd);
        TrackPopupMenu(menu, 0x0080, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
    }

    private void Add()
    {
        if (_shown) return;
        var data = Data();
        data.uFlags = 0x1 | 0x2 | 0x4;
        data.uCallbackMessage = WmTray;
        data.hIcon = LoadAppIcon();
        data.szTip = _text("AppTitle");
        Shell_NotifyIcon(0, ref data);
        _shown = true;
    }

    private void Remove()
    {
        if (!_shown) return;
        var data = Data();
        Shell_NotifyIcon(2, ref data);
        _shown = false;
    }

    private NotifyIconData Data() => new() { cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = _hwnd, uID = 1 };

    private static IntPtr LoadAppIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } path)
            {
                var large = new IntPtr[1];
                var small = new IntPtr[1];
                if (ExtractIconEx(path, 0, large, small, 1) > 0 && small[0] != IntPtr.Zero)
                    return small[0];
            }
        }
        catch (Exception) { }
        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    public void Dispose() => Remove();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, int flags, int id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool TrackPopupMenu(IntPtr menu, int flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
}

/// <summary>
/// Atajos globales con RegisterHotKey sobre la ventana principal (RF-05, RF-12). No hace falta
/// gancho: Windows manda WM_HOTKEY.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    public const int RecordId = 1, PlayId = 2;
    private const int WmHotkey = 0x0312, ModNoRepeat = 0x4000;

    private readonly IntPtr _hwnd;
    private readonly HashSet<int> _registered = [];

    public HotkeyManager(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
    }

    public event Action<int>? Pressed;

    /// <summary>Registra (o cambia) un atajo. Falso si otro programa ya lo tiene.</summary>
    public bool Register(int id, Input.Hotkey hotkey)
    {
        Unregister(id);
        if (!hotkey.IsValid || !RegisterHotKey(_hwnd, id, (int)hotkey.Modifiers | ModNoRepeat, hotkey.Key))
            return false;
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
            UnregisterHotKey(_hwnd, id);
    }

    public bool IsRegistered(int id) => _registered.Contains(id);

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            Pressed?.Invoke((int)wParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered.ToList())
            Unregister(id);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
