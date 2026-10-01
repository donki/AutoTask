using SocAutoTask.Model;
using SocAutoTask.Playback;

// Loc (el idioma) y AppPaths.Current son estaticos: las pruebas no corren en paralelo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SocAutoTask.Tests;

/// <summary>Reloj falso: esperar es avanzar al instante pedido. <see cref="Advanced"/> deja parar a mitad.</summary>
internal sealed class FakeClock : IClock
{
    public double NowMs { get; set; }

    public Action<double>? Advanced { get; set; }

    public int HighResolutionCount { get; private set; }

    public bool WaitUntil(double targetMs, PlaybackControl control)
    {
        if (control.IsStopRequested)
            return false;
        if (targetMs > NowMs)
            NowMs = targetMs;
        Advanced?.Invoke(NowMs);
        return !control.IsStopRequested;
    }

    public IDisposable HighResolution()
    {
        HighResolutionCount++;
        return new Nothing();
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>SendInput falso: apunta que se envio y cuando.</summary>
internal sealed class RecordingSink(FakeClock? clock = null) : IInputSink
{
    public List<(MacroEvent Event, double At)> Sent { get; } = [];

    public Action<MacroEvent>? OnSend { get; set; }

    public void Send(in MacroEvent e)
    {
        Sent.Add((e, clock?.NowMs ?? 0));
        OnSend?.Invoke(e);
    }
}

/// <summary>Teclado falso: modificadores pulsados hasta cierto instante.</summary>
internal sealed class FakeKeyboard(FakeClock clock, double releaseAt) : IKeyboardState
{
    public int Asked { get; private set; }

    public bool AnyModifierDown()
    {
        Asked++;
        return clock.NowMs < releaseAt;
    }
}

/// <summary>Carpeta temporal que se borra al acabar.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sOCAutoTask-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class Samples
{
    /// <summary>Una grabacion con un evento de cada tipo, coordenadas negativas (monitor a la izquierda) y teclas extendidas.</summary>
    public static List<MacroEvent> AllKinds() =>
    [
        MacroEvent.Move(-1900, 15),
        MacroEvent.Move(-1890, 20, 16),
        MacroEvent.Down(MouseButton.Left, -1890, 20, 100),
        MacroEvent.Up(MouseButton.Left, -1890, 20, 80),
        MacroEvent.Down(MouseButton.Right, 300, 400, 5),
        MacroEvent.Up(MouseButton.Right, 300, 400, 5),
        MacroEvent.Down(MouseButton.Middle, 1919, 1079, 5),
        MacroEvent.Up(MouseButton.Middle, 1919, 1079, 5),
        MacroEvent.Down(MouseButton.X1, 0, 0, 5),
        MacroEvent.Up(MouseButton.X1, 0, 0, 5),
        MacroEvent.Down(MouseButton.X2, 1, 1, 5),
        MacroEvent.Up(MouseButton.X2, 1, 1, 5),
        MacroEvent.WheelAt(10, 10, -240, 30),
        MacroEvent.WheelAt(10, 10, 120, 30, horizontal: true),
        MacroEvent.Key(true, 0x10, 0x2A, delay: 200),
        MacroEvent.Key(true, 0x41, 0x1E, delay: 50),
        MacroEvent.Key(false, 0x41, 0x1E, delay: 60),
        MacroEvent.Key(false, 0x10, 0x2A, delay: 10),
        MacroEvent.Key(true, 0x25, 0x4B, extended: true, delay: 10),
        MacroEvent.Key(false, 0x25, 0x4B, extended: true, delay: 10),
        MacroEvent.WaitFor(1500),
        MacroEvent.Move(int.MaxValue, int.MinValue, int.MaxValue / 4),
    ];

    public static Recording Recording(PlaybackOptions? options = null) =>
        new(AllKinds(), options ?? new PlaybackOptions { Speed = 2.5, Repeat = RepeatMode.Times, Times = 7, PauseBetweenMs = 1234 },
            new ScreenRect(-1920, 0, 3840, 1080), new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

    public static byte[] Bytes(Recording recording)
    {
        using var ms = new MemoryStream();
        Format.RecordingFile.Write(recording, ms);
        return ms.ToArray();
    }
}
