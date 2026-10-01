using SocAutoTask.Format;

namespace SocAutoTask.AppServices;

/// <summary>
/// Traduce un fallo de fichero a la clave de Loc con la razon y que hacer (§6.9). Lo tecnico va
/// al registro, nunca a la cara del usuario.
/// </summary>
public static class FileErrors
{
    private const int DiskFull = 0x70, HandleDiskFull = 0x27, SharingViolation = 0x20, LockViolation = 0x21;

    public static string LocKey(Exception ex) => ex switch
    {
        RecordingFormatException f => f.LocKey,
        UnauthorizedAccessException => "FileNoPermission",
        FileNotFoundException or DirectoryNotFoundException => "FileNotFound",
        PathTooLongException => "FilePathTooLong",
        IOException io when (io.HResult & 0xFFFF) is DiskFull or HandleDiskFull => "FileDiskFull",
        IOException io when (io.HResult & 0xFFFF) is SharingViolation or LockViolation => "FileInUse",
        InvalidDataException => "FormatCorrupt",
        _ => "FileGenericError",
    };
}
