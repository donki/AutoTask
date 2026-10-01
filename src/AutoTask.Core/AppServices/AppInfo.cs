using System.Reflection;
using System.Runtime.InteropServices;

namespace SocAutoTask.AppServices;

/// <summary>Version y datos fijos de la aplicacion.</summary>
public static partial class AppInfo
{
    public const string ExeName = "sOCAutoTask";
    public const string ProductName = "sOC AutoTask";
    public const string ContactAddress = "mailto:jsoladelarosa@gmail.com";
    public const string ReleasesUrl = "https://github.com/donki/AutoTask/releases";

    /// <summary>La version de Directory.Build.props tal cual (con sus ceros: 2026.10.01.0).</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var plus = text.IndexOf('+');
        return plus > 0 ? text[..plus] : text;
    }

    /// <summary>
    /// ¿Va dentro de un paquete MSIX? Entonces la asociacion de ficheros la pone el paquete y no
    /// se escribe en el registro.
    /// </summary>
    public static bool IsPackaged
    {
        get
        {
            try
            {
                var length = 0u;
                return GetCurrentPackageFullName(ref length, 0) != AppModelErrorNoPackage;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    private const int AppModelErrorNoPackage = 15700;

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint length, nint buffer);
}
