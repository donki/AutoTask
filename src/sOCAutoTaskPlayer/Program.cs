using SocAutoTask.Compile;
using SocAutoTask.Format;
using SocAutoTask.Input;
using SocAutoTask.Localization;
using SocAutoTask.Native;
using SocAutoTask.Playback;

namespace SocAutoTask.Player;

/// <summary>
/// El exe compilado (RF-29..31): se lee a si mismo, encuentra la grabacion, pone la parada de
/// emergencia y la reproduce con su velocidad y repeticiones. Sin ventana; los errores, en un aviso
/// en el idioma de Windows. <c>--check</c> solo valida (codigo 0 si esta bien) y no reproduce.
/// </summary>
internal static class Program
{
#if !AUTOTASK_TESTS
    [STAThread]
    private static int Main(string[] args) => Run(args, Environment.ProcessPath, PlayerHost.Real);
#endif

    /// <summary>Todo menos el punto de entrada: las pruebas lo llaman con un exe y un sistema falsos.</summary>
    internal static int Run(string[] args, string? exe, PlayerHost host)
    {
        Loc.Use(Loc.SystemLanguage());
        var check = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
        if (exe is null)
            return 2;

        Model.Recording recording;
        ExeOptions options;
        try
        {
            (recording, options) = PayloadReader.Read(exe);
            if (recording.IsEmpty)
                throw new RecordingFormatException(FormatProblem.Empty, "sin eventos");
        }
        catch (RecordingFormatException ex) when (ex.Problem == FormatProblem.NotARecording)
        {
            if (!check)
                host.Error(Loc.Get("PlayerNoRecording"), Loc.Get("PlayerTitle"));
            return 3;
        }
        catch (Exception ex) when (ex is RecordingFormatException or IOException or UnauthorizedAccessException)
        {
            if (!check)
                host.Error(Loc.Format("PlayerBadRecording", Loc.Get(SocAutoTask.AppServices.FileErrors.LocKey(ex))), Loc.Get("PlayerTitle"));
            return 4;
        }

        if (check)
        {
            host.Out.WriteLine($"OK {recording.Count} {recording.TotalMs} {recording.Options.Speed} {recording.Options.Repeat} {recording.Options.Times}");
            return 0;
        }

        var session = new PlaybackSession(recording.Events, recording.Options, host.Sink(), host.Clock(), host.Keyboard, options.CountdownSeconds);
        using var emergency = host.Emergency(options.Emergency, options.EscapeHoldMs, () => session.Control.Stop(StopReason.Emergency));
        if (options.Emergency != EmergencyKeys.None)
        {
            try
            {
                emergency.Start();
            }
            catch (Exception)
            {
                host.Error(Loc.Get("PlayerHookFailed"), Loc.Get("PlayerTitle"));
                return 5;
            }
        }
        var reason = session.Run();
        return reason == StopReason.None ? 0 : 1;
    }
}

/// <summary>Lo que el reproductor pide al sistema (de verdad, o un doble en las pruebas).</summary>
internal sealed record PlayerHost(
    Action<string, string> Error,
    TextWriter Out,
    Func<IInputSink> Sink,
    Func<IClock> Clock,
    IKeyboardState Keyboard,
    Func<EmergencyKeys, int, Action, IEmergencyWatcher> Emergency)
{
    public static PlayerHost Real { get; } = new(
        NativeDialogs.Error,
        Console.Out,
        () => new Win32InputSink(),
        () => new StopwatchClock(),
        new SystemKeyboardState(),
        (keys, ms, stop) => new EmergencyStopWatcher(keys, ms, stop));
}
