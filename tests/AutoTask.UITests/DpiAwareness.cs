using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SocAutoTask.UITests;

/// <summary>
/// El proceso de las pruebas (testhost) no trae manifiesto per-monitor: se pide al cargar el
/// ensamblado, para que el cursor, SendInput y UI Automation hablen en pixeles fisicos, como la
/// aplicacion (RF-04).
/// </summary>
internal static class DpiAwareness
{
    [ModuleInitializer]
    internal static void Init() => SetProcessDpiAwarenessContext(new IntPtr(-4));   // PER_MONITOR_AWARE_V2

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
