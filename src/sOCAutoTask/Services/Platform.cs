using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using SocAutoTask.AppServices;
using SocAutoTask.Input;
using SocAutoTask.Native;
using SocAutoTask.Playback;

namespace SocAutoTask.Desktop.Services;

/// <summary>
/// Lo que las ventanas piden al sistema: ganchos, SendInput, elevacion, abrir programas, elegir
/// ficheros, el registro de la asociacion y la instancia unica. En la aplicacion es
/// <see cref="RealPlatform"/>; las pruebas ponen un doble en <see cref="Platform.Current"/> y asi
/// prueban las ventanas sin mover el raton, sin ganchos y sin tocar nada de quien trabaja.
/// </summary>
public interface IPlatform
{
    IRecorder CreateRecorder(RecorderOptions options);

    ICountingSink CreateSink();

    IClock CreateClock();

    IKeyboardState Keyboard { get; }

    IEmergencyWatcher CreateEmergency(EmergencyKeys keys, int escapeHoldMs, Action onStop);

    bool IsElevated { get; }

    bool ForegroundIsElevatedAndWeAreNot();

    /// <summary>Abre un programa, una carpeta o un enlace (lanza como Process.Start).</summary>
    void Start(ProcessStartInfo info);

    /// <summary>Dialogo de abrir. Null si se cancela.</summary>
    string? PickOpenFile(Window owner, string filter, string folder);

    /// <summary>Dialogo de guardar. Null si se cancela.</summary>
    string? PickSaveFile(Window owner, string filter, string fileName, string folder, string defaultExt);

    /// <summary>Raiz del registro para la asociacion de .soctask (HKCU en la aplicacion).</summary>
    RegistryKey AssociationRoot { get; }

    /// <summary>El exe de la aplicacion (para la asociacion y para reiniciar como administrador).</summary>
    string? ExePath { get; }

    bool IsPackaged { get; }

    SingleInstance CreateInstance(string version);
}

/// <summary>Donde esta la plataforma de esta ejecucion.</summary>
public static class Platform
{
    public static IPlatform Current { get; set; } = new RealPlatform();
}

/// <summary>La de verdad: ganchos LL, SendInput, dialogos comunes de Windows y HKCU.</summary>
public sealed class RealPlatform : IPlatform
{
    public IRecorder CreateRecorder(RecorderOptions options) => new Recorder(options);

    public ICountingSink CreateSink() => new Win32InputSink();

    public IClock CreateClock() => new StopwatchClock();

    public IKeyboardState Keyboard { get; } = new SystemKeyboardState();

    public IEmergencyWatcher CreateEmergency(EmergencyKeys keys, int escapeHoldMs, Action onStop) =>
        new EmergencyStopWatcher(keys, escapeHoldMs, onStop);

    public bool IsElevated => Elevation.IsCurrentProcessElevated();

    public bool ForegroundIsElevatedAndWeAreNot() => Elevation.ForegroundIsElevatedAndWeAreNot();

    public void Start(ProcessStartInfo info) => Process.Start(info)?.Dispose();

    public string? PickOpenFile(Window owner, string filter, string folder)
    {
        var dialog = new OpenFileDialog { Filter = filter, InitialDirectory = folder };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(Window owner, string filter, string fileName, string folder, string defaultExt)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = fileName,
            InitialDirectory = folder,
            AddExtension = true,
            DefaultExt = defaultExt,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public RegistryKey AssociationRoot => Registry.CurrentUser;

    public string? ExePath => Environment.ProcessPath;

    public bool IsPackaged => AppInfo.IsPackaged;

    public SingleInstance CreateInstance(string version) => new(version);
}
