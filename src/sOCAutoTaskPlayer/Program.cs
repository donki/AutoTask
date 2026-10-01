using SocAutoTask.Compile;
using SocAutoTask.Format;
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
    [STAThread]
    private static int Main(string[] args)
    {
        Loc.Use(Loc.SystemLanguage());
        var check = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
        var exe = Environment.ProcessPath;
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
                NativeDialogs.Error(Loc.Get("PlayerNoRecording"), Loc.Get("PlayerTitle"));
            return 3;
        }
        catch (Exception ex) when (ex is RecordingFormatException or IOException or UnauthorizedAccessException)
        {
            if (!check)
                NativeDialogs.Error(Loc.Format("PlayerBadRecording", Loc.Get(SocAutoTask.AppServices.FileErrors.LocKey(ex))), Loc.Get("PlayerTitle"));
            return 4;
        }

        if (check)
        {
            Console.Out.WriteLine($"OK {recording.Count} {recording.TotalMs} {recording.Options.Speed} {recording.Options.Repeat} {recording.Options.Times}");
            return 0;
        }

        var session = new PlaybackSession(recording.Events, recording.Options, new Win32InputSink(), new StopwatchClock(), new SystemKeyboardState(), options.CountdownSeconds);
        using var emergency = new EmergencyStopWatcher(options.Emergency, options.EscapeHoldMs, () => session.Control.Stop(StopReason.Emergency));
        if (options.Emergency != SocAutoTask.Input.EmergencyKeys.None)
        {
            try
            {
                emergency.Start();
            }
            catch (Exception)
            {
                NativeDialogs.Error(Loc.Get("PlayerHookFailed"), Loc.Get("PlayerTitle"));
                return 5;
            }
        }
        var reason = session.Run();
        return reason == StopReason.None ? 0 : 1;
    }
}
