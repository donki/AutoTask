namespace SocAutoTask.AppServices;

/// <summary>
/// Donde guarda sus cosas (RF-48): junto al exe si hay un <c>portable.ini</c>; si no, en
/// <c>%LOCALAPPDATA%\sOCAutoTask</c>.
/// </summary>
public sealed class AppPaths
{
    public const string PortableMarker = "portable.ini";

    private AppPaths(string folder, bool portable)
    {
        DataFolder = folder;
        Portable = portable;
    }

    public string DataFolder { get; }

    public bool Portable { get; }

    public string SettingsFile => Path.Combine(DataFolder, "settings.json");

    /// <summary>Copia de una grabacion sin guardar que dejo la version anterior al ser sustituida (§8.3).</summary>
    public string RecoveryFile => Path.Combine(DataFolder, "recuperada.soctask");

    /// <summary>La de esta ejecucion (la pone App al arrancar; las pruebas, una temporal).</summary>
    public static AppPaths Current { get; set; } = Resolve(AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), File.Exists);

    public static AppPaths Resolve(string exeFolder, string localAppData, Func<string, bool> fileExists)
    {
        if (fileExists(Path.Combine(exeFolder, PortableMarker)))
            return new AppPaths(exeFolder, portable: true);
        return new AppPaths(Path.Combine(localAppData, AppInfo.ExeName), portable: false);
    }

    /// <summary>Una carpeta concreta (modo aislado, pruebas).</summary>
    public static AppPaths At(string folder) => new(folder, portable: false);
}

/// <summary>
/// Registro de errores en <c>errors.log</c> de la carpeta de datos (§6.9 y §6.12): lo tecnico va
/// aqui, con la traza, y no a la cara del usuario. Al pasar de 1 MB se rota a <c>errors.old.log</c>.
/// Nunca se registra lo grabado (seria lo que la persona tecleo).
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string Folder => AppPaths.Current.DataFolder;

    public static string FilePath => Path.Combine(Folder, "errors.log");

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1024 * 1024)
                    File.Move(FilePath, Path.Combine(Folder, "errors.old.log"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Registrar nunca puede ser otro error.
        }
    }
}
