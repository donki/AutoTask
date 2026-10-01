using SocAutoTask.Format;
using SocAutoTask.Localization;
using SocAutoTask.Model;

namespace SocAutoTask.AppServices;

/// <summary>
/// La grabacion con la que se trabaja: de donde vino, si tiene cambios sin guardar (RF-08, RF-50)
/// y como se llama. Sin interfaz, para probarla.
/// </summary>
public sealed class MacroSession
{
    public Recording? Recording { get; private set; }

    /// <summary>Fichero .soctask donde esta guardada; null si es nueva o importada.</summary>
    public string? FilePath { get; private set; }

    /// <summary>De donde se abrio (para el nombre que se propone al guardar).</summary>
    public string? SourcePath { get; private set; }

    public LoadedKind Kind { get; private set; }

    public bool Dirty { get; private set; }

    public bool HasRecording => Recording is { IsEmpty: false };

    public event Action? Changed;

    public string DisplayName => FilePath is not null
        ? Path.GetFileNameWithoutExtension(FilePath)
        : SourcePath is not null ? Path.GetFileNameWithoutExtension(SourcePath) : Loc.Get("Untitled");

    /// <summary>Nombre que se propone al guardar o compilar.</summary>
    public string SuggestedName(string extension) =>
        (FilePath ?? SourcePath) is { } p ? Path.GetFileNameWithoutExtension(p) + extension : Loc.Get("UntitledFile") + extension;

    /// <summary>Carpeta que se propone al guardar o abrir.</summary>
    public string? SuggestedFolder => (FilePath ?? SourcePath) is { } p ? Path.GetDirectoryName(p) : null;

    public void SetRecorded(Recording recording)
    {
        Recording = recording;
        FilePath = SourcePath = null;
        Kind = LoadedKind.SocTask;
        Dirty = true;
        Changed?.Invoke();
    }

    /// <summary>Abierta de un fichero. Lo importado (.rec, exe) cuenta como sin guardar: aun no es un .soctask.</summary>
    public void SetOpened(Recording recording, string path, LoadedKind kind)
    {
        Recording = recording;
        Kind = kind;
        SourcePath = path;
        FilePath = kind == LoadedKind.SocTask ? path : null;
        Dirty = kind != LoadedKind.SocTask;
        Changed?.Invoke();
    }

    public void MarkSaved(string path)
    {
        FilePath = SourcePath = path;
        Kind = LoadedKind.SocTask;
        Dirty = false;
        Changed?.Invoke();
    }

    public void ReplaceEvents(IEnumerable<MacroEvent> events)
    {
        if (Recording is null)
            return;
        Recording = Recording.WithEvents(events);
        Dirty = true;
        Changed?.Invoke();
    }

    public void SetOptions(PlaybackOptions options)
    {
        if (Recording is null)
            return;
        var normalized = options.Normalized();
        if (normalized == Recording.Options)
            return;
        Recording.Options = normalized;
        Dirty = true;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Recording = null;
        FilePath = SourcePath = null;
        Dirty = false;
        Changed?.Invoke();
    }
}
