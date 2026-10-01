using SocAutoTask.Compile;
using SocAutoTask.Model;

namespace SocAutoTask.Format;

/// <summary>De donde vino una grabacion abierta.</summary>
public enum LoadedKind
{
    SocTask,
    /// <summary>Importada de un .rec (experimental): al guardar se pide nombre .soctask.</summary>
    Rec,
    /// <summary>Sacada de un exe compilado por AutoTask.</summary>
    CompiledExe,
}

/// <summary>Abre lo que se le de (RF-26, RF-28): .soctask, .rec o un exe compilado por AutoTask.</summary>
public static class RecordingLoader
{
    public static (Recording Recording, LoadedKind Kind) Open(string path)
    {
        var extension = Path.GetExtension(path);
        Recording recording;
        LoadedKind kind;
        if (extension.Equals(RecImporter.Extension, StringComparison.OrdinalIgnoreCase))
        {
            recording = RecImporter.Load(path);
            kind = LoadedKind.Rec;
        }
        else if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            recording = PayloadReader.Read(path).Recording;
            kind = LoadedKind.CompiledExe;
        }
        else
        {
            recording = RecordingFile.Load(path);
            kind = LoadedKind.SocTask;
        }
        if (recording.IsEmpty)
            throw new RecordingFormatException(FormatProblem.Empty, "sin eventos");
        return (recording, kind);
    }

    /// <summary>¿Se puede abrir por la extension? (para arrastrar y soltar)</summary>
    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(RecordingFile.Extension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(RecImporter.Extension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }
}
