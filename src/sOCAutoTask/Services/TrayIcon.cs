using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace SocAutoTask.Desktop.Services;

/// <summary>
/// Icono en el area de notificacion (RF-43), como el de RC Manager: al minimizar la ventana se
/// esconde y queda el icono; clic para volver, boton derecho para Abrir, Grabar/Parar,
/// Reproducir/Parar o Salir. Shell_NotifyIcon y un gancho en el procedimiento de la ventana; el
/// menu es un ContextMenu de WPF con el aspecto comun de las apps sOC (General 6.14), no el gris
/// del sistema.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WmSysCommand = 0x0112, WmRButtonUp = 0x0205, WmLButtonUp = 0x0202, WmLButtonDblClk = 0x0203, WmContextMenu = 0x007B;
    private const int WmTray = 0x8001;
    private const int ScMinimize = 0xF020;

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly Func<string, string> _text;
    private readonly Func<ContextMenu> _buildMenu;
    private bool _shown;

    public bool MinimizeToTray { get; set; } = true;

    /// <param name="buildMenu">Crea el menu del boton derecho cada vez que se abre (asi va al dia).</param>
    public TrayIcon(Window window, Func<string, string> text, Func<ContextMenu> buildMenu)
    {
        _window = window;
        _text = text;
        _buildMenu = buildMenu;
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
                else if (evt is WmRButtonUp or WmContextMenu) ShowMenu();
                handled = true;
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

    /// <summary>
    /// Abre el menu junto al puntero. Al abrirse pasa a primer plano: si no, no se cierra al
    /// pulsar fuera (la ventana principal esta escondida).
    /// </summary>
    private void ShowMenu()
    {
        var menu = _buildMenu();
        menu.Placement = PlacementMode.MousePoint;
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource popup)
                SetForegroundWindow(popup.Handle);
        };
        menu.IsOpen = true;
    }

    /// <summary>Quita el icono (al salir desde su menu, antes de cerrar la ventana).</summary>
    public void Hide() => Remove();

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

    /// <summary>
    /// Un elemento de menu con el estilo comun (General 6.14) y su glifo de Segoe Fluent Icons:
    /// en el color de acento, o en Danger si la accion es destructiva o de grabar. Los colores van
    /// por referencia al recurso para seguir el tema.
    /// </summary>
    public static MenuItem Item(string text, string glyph, Action action, bool enabled = true, bool danger = false)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = 14,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, danger ? "Danger" : "Primary");
        var item = new MenuItem
        {
            Header = text,
            Style = (Style)Application.Current.FindResource("TrayItem"),
            IsEnabled = enabled,
            Icon = icon,
        };
        item.Click += (_, _) => action();
        return item;
    }

    public static Separator Separator() => new() { Style = (Style)Application.Current.FindResource("TraySeparator") };

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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
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
