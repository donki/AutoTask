using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SocAutoTask.AppServices;

/// <summary>
/// Asociacion de <c>.soctask</c> con AutoTask para este usuario (RF-27): claves en
/// <c>HKCU\Software\Classes</c>, nada de la maquina. Dentro del MSIX la pone el paquete.
/// </summary>
public static partial class FileAssociation
{
    public const string ProgId = "sOCAutoTask.Recording";
    public const string ClassesRoot = @"Software\Classes";

    /// <summary>Una clave con su valor (null = valor por defecto de la clave).</summary>
    public readonly record struct Entry(string Key, string? Name, string Value);

    /// <summary>Lo que hay que escribir para asociar la extension con ese exe.</summary>
    public static IReadOnlyList<Entry> Entries(string exePath, string description) =>
    [
        new(@"Software\Classes\.soctask", null, ProgId),
        new($@"Software\Classes\{ProgId}", null, description),
        new($@"Software\Classes\{ProgId}\DefaultIcon", null, $"\"{exePath}\",0"),
        new($@"Software\Classes\{ProgId}\shell\open\command", null, Command(exePath)),
    ];

    public static string Command(string exePath) => $"\"{exePath}\" \"%1\"";

    /// <summary>¿Esta asociada ahora con este exe?</summary>
    public static bool IsApplied(string exePath, RegistryKey? root = null)
    {
        root ??= Registry.CurrentUser;
        try
        {
            using var ext = root.OpenSubKey(@"Software\Classes\.soctask");
            if (ext?.GetValue(null) as string != ProgId)
                return false;
            using var command = root.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
            return string.Equals(command?.GetValue(null) as string, Command(exePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static void Apply(string exePath, string description, RegistryKey? root = null)
    {
        root ??= Registry.CurrentUser;
        foreach (var e in Entries(exePath, description))
        {
            using var key = root.CreateSubKey(e.Key, writable: true);
            key.SetValue(e.Name ?? string.Empty, e.Value);
        }
        NotifyShell();
    }

    /// <summary>Quita la asociacion (solo si es la nuestra: no se borra la de otro programa).</summary>
    public static void Remove(RegistryKey? root = null)
    {
        root ??= Registry.CurrentUser;
        using (var ext = root.OpenSubKey(@"Software\Classes\.soctask", writable: true))
        {
            if (ext is not null && ext.GetValue(null) as string == ProgId)
            {
                ext.Close();
                root.DeleteSubKeyTree(@"Software\Classes\.soctask", throwOnMissingSubKey: false);
            }
        }
        root.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        NotifyShell();
    }

    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, 0, 0);
        }
        catch (Exception)
        {
            // Solo refresca los iconos del Explorador.
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
