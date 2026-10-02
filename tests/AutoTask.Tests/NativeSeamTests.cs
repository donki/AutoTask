using System.Diagnostics;
using SocAutoTask.AppServices;
using SocAutoTask.Compile;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Input;
using SocAutoTask.Model;
using SocAutoTask.Native;
using SocAutoTask.Playback;
using SocAutoTask.Player;

namespace SocAutoTask.Tests;

/// <summary>
/// Lo que va contra Windows, sin Windows: los INPUT que saldrian por SendInput, lo que harian los
/// ganchos con cada mensaje, el reproductor de los exe compilados y la plataforma de verdad (solo
/// lo que no cambia nada: crear objetos y preguntar).
/// </summary>
public sealed class NativeSeamTests
{
    private static readonly ScreenRect TwoMonitors = new(-1920, 0, 3840, 1080);

    private static (Win32InputSink Sink, List<Win32.INPUT> Sent) Sink(bool accept = true)
    {
        var sent = new List<Win32.INPUT>();
        var sink = new Win32InputSink((in Win32.INPUT i) => { sent.Add(i); return accept; }, () => TwoMonitors);
        return (sink, sent);
    }

    [Fact]
    public void SendInput_Raton_CoordenadasAbsolutasDelEscritorioVirtual_YBotones()
    {
        var (sink, sent) = Sink();
        sink.Send(MacroEvent.Move(-1920, 0));
        sink.Send(MacroEvent.Down(MouseButton.Left, 0, 540));
        sink.Send(MacroEvent.Up(MouseButton.Left, 0, 540));
        sink.Send(MacroEvent.Down(MouseButton.Right, 1, 1));
        sink.Send(MacroEvent.Up(MouseButton.Right, 1, 1));
        sink.Send(MacroEvent.Down(MouseButton.Middle, 1, 1));
        sink.Send(MacroEvent.Up(MouseButton.Middle, 1, 1));
        sink.Send(MacroEvent.Down(MouseButton.X1, 1, 1));
        sink.Send(MacroEvent.Up(MouseButton.X2, 1, 1));
        sink.Send(MacroEvent.WheelAt(5, 5, -120));
        sink.Send(MacroEvent.WheelAt(5, 5, 240, horizontal: true));
        sink.Send(MacroEvent.Move(99999, 99999));   // fuera: se queda en el borde

        const uint abs = Win32.MOUSEEVENTF_MOVE | Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK;
        Assert.All(sent, i => Assert.Equal(Win32.INPUT_MOUSE, i.type));
        Assert.All(sent, i => Assert.Equal(Win32InputSink.Signature, i.u.mi.dwExtraInfo));
        Assert.Equal(0, sent[0].u.mi.dx);
        Assert.Equal(CoordinateMapper.Normalize(0, -1920, 3840), sent[1].u.mi.dx);
        Assert.Equal(CoordinateMapper.Normalize(540, 0, 1080), sent[1].u.mi.dy);
        uint[] flags =
        [
            0, Win32.MOUSEEVENTF_LEFTDOWN, Win32.MOUSEEVENTF_LEFTUP, Win32.MOUSEEVENTF_RIGHTDOWN, Win32.MOUSEEVENTF_RIGHTUP,
            Win32.MOUSEEVENTF_MIDDLEDOWN, Win32.MOUSEEVENTF_MIDDLEUP, Win32.MOUSEEVENTF_XDOWN, Win32.MOUSEEVENTF_XUP,
            Win32.MOUSEEVENTF_WHEEL, Win32.MOUSEEVENTF_HWHEEL, 0,
        ];
        Assert.Equal(flags.Select(f => abs | f), sent.Select(i => i.u.mi.dwFlags));
        Assert.Equal(1u, sent[7].u.mi.mouseData);   // X1
        Assert.Equal(2u, sent[8].u.mi.mouseData);   // X2
        Assert.Equal(unchecked((uint)-120), sent[9].u.mi.mouseData);
        Assert.Equal(240u, sent[10].u.mi.mouseData);
        Assert.Equal(CoordinateMapper.Normalize(1919, -1920, 3840), sent[11].u.mi.dx);
        Assert.Equal(0, sink.Rejected);
    }

    [Fact]
    public void SendInput_Teclas_ConScanCode_Extendidas_YSinScanSeCalcula()
    {
        var (sink, sent) = Sink();
        sink.Send(MacroEvent.Key(true, 0x41, 0x1E));
        sink.Send(MacroEvent.Key(false, 0x25, 0x4B, extended: true));
        sink.Send(MacroEvent.Key(true, 0x41, 0));
        sink.Send(MacroEvent.WaitFor(100));   // una espera no envia nada

        Assert.Equal(3, sent.Count);
        Assert.All(sent, i => Assert.Equal(Win32.INPUT_KEYBOARD, i.type));
        Assert.Equal((ushort)0x41, sent[0].u.ki.wVk);
        Assert.Equal((ushort)0x1E, sent[0].u.ki.wScan);
        Assert.Equal(0u, sent[0].u.ki.dwFlags);
        Assert.Equal(Win32.KEYEVENTF_EXTENDEDKEY | Win32.KEYEVENTF_KEYUP, sent[1].u.ki.dwFlags);
        Assert.Equal((ushort)Win32.MapVirtualKey(0x41, 0), sent[2].u.ki.wScan);
        Assert.NotEqual((ushort)0, sent[2].u.ki.wScan);
    }

    [Fact]
    public void SendInput_LoRechazado_SeCuenta_YLaPantallaSeVuelveAMirar()
    {
        var screens = new Queue<ScreenRect>([new ScreenRect(0, 0, 100, 100), new ScreenRect(0, 0, 200, 200)]);
        var sent = new List<Win32.INPUT>();
        var sink = new Win32InputSink((in Win32.INPUT i) => { sent.Add(i); return false; }, () => screens.Dequeue()) { ScreenCheckMs = 0 };
        sink.Send(MacroEvent.Move(150, 150));
        Assert.Equal(1, sink.Rejected);
        Assert.Equal(CoordinateMapper.Normalize(150, 0, 200), sent[0].u.mi.dx);   // ya con la pantalla nueva
        Assert.Equal(0, new Win32InputSink().Rejected);
    }

    private static Win32.MSLLHOOKSTRUCT Mouse(int x, int y, uint data = 0, uint flags = 0, uint time = 0) =>
        new() { pt = new Win32.POINT { X = x, Y = y }, mouseData = data, flags = flags, time = time };

    private static Win32.KBDLLHOOKSTRUCT Kbd(uint vk, uint scan = 0, uint flags = 0, uint time = 0) =>
        new() { vkCode = vk, scanCode = scan, flags = flags, time = time };

    [Fact]
    public void Gancho_DeGrabar_CadaMensaje_SuEvento()
    {
        using var recorder = new Recorder(new RecorderOptions());
        recorder.Simulate(Win32.WM_MOUSEMOVE, Mouse(1, 2, time: 1000));
        recorder.Simulate(Win32.WM_LBUTTONDOWN, Mouse(1, 2, time: 1010));
        recorder.Simulate(Win32.WM_LBUTTONUP, Mouse(1, 2, time: 1020));
        recorder.Simulate(Win32.WM_RBUTTONDOWN, Mouse(1, 2, time: 1030));
        recorder.Simulate(Win32.WM_RBUTTONUP, Mouse(1, 2, time: 1040));
        recorder.Simulate(Win32.WM_MBUTTONDOWN, Mouse(1, 2, time: 1050));
        recorder.Simulate(Win32.WM_MBUTTONUP, Mouse(1, 2, time: 1060));
        recorder.Simulate(Win32.WM_XBUTTONDOWN, Mouse(1, 2, data: 2u << 16, time: 1070));
        recorder.Simulate(Win32.WM_XBUTTONUP, Mouse(1, 2, data: 2u << 16, time: 1080));
        recorder.Simulate(Win32.WM_MOUSEWHEEL, Mouse(1, 2, data: unchecked((uint)(-120 << 16)), time: 1090));
        recorder.Simulate(Win32.WM_MOUSEHWHEEL, Mouse(1, 2, data: 120u << 16, time: 1100));
        recorder.Simulate(0x02A3, Mouse(1, 2, time: 1110));                 // otro mensaje: nada
        recorder.Simulate(Win32.WM_MOUSEMOVE, Mouse(5, 5, flags: Win32.LLMHF_INJECTED, time: 1120));   // inyectado: nada
        recorder.Simulate(Win32.WM_KEYDOWN, Kbd(0x41, 0x1E, time: 1200));
        recorder.Simulate(Win32.WM_KEYUP, Kbd(0x41, 0x1E, time: 1210));
        recorder.Simulate(Win32.WM_SYSKEYDOWN, Kbd(0x25, 0x4B, Win32.LLKHF_EXTENDED, time: 900));   // reloj hacia atras: sin espera
        recorder.Simulate(Win32.WM_SYSKEYUP, Kbd(0x25, 0x4B, Win32.LLKHF_EXTENDED, time: 950));
        recorder.Simulate(Win32.WM_KEYDOWN, Kbd(0x42, flags: Win32.LLKHF_INJECTED, time: 1300));  // inyectado: nada
        recorder.Simulate(0x0102, Kbd(0x42, time: 1300));                   // WM_CHAR: nada

        Assert.Equal(15, recorder.Count);
        var r = recorder.StopAndCollect(new PlaybackOptions());
        var e = r.Events;
        Assert.Equal(EventKind.MouseMove, e[0].Kind);
        Assert.Equal(10, e[1].DelayMs);
        Assert.Equal([MouseButton.Left, MouseButton.Left, MouseButton.Right, MouseButton.Right, MouseButton.Middle, MouseButton.Middle, MouseButton.X2, MouseButton.X2],
            e.Skip(1).Take(8).Select(x => x.Button));
        Assert.Equal(EventKind.Wheel, e[9].Kind);
        Assert.Equal(-120, e[9].Data);
        Assert.Equal(EventKind.HWheel, e[10].Kind);
        Assert.Equal(0x41, e[11].VirtualKey);
        Assert.Equal(EventKind.KeyUp, e[12].Kind);
        Assert.Equal(0, e[13].DelayMs);
        Assert.True(e[13].IsExtended);
        Assert.Equal(50, e[14].DelayMs);
    }

    [Fact]
    public void Gancho_DeGrabar_SinMovimientos_YLaVentanaPropiaNoSeGraba()
    {
        using var recorder = new Recorder(new RecorderOptions { MouseMoves = false, IncludeInjected = true });
        recorder.SetIgnoredArea(new ScreenRect(0, 0, 100, 100));
        recorder.Simulate(Win32.WM_MOUSEMOVE, Mouse(10, 10));
        recorder.Simulate(Win32.WM_LBUTTONDOWN, Mouse(10, 10));                       // dentro: no
        recorder.Simulate(Win32.WM_LBUTTONDOWN, Mouse(500, 10, flags: Win32.LLMHF_INJECTED));   // fuera e inyectado (E2E): si
        recorder.SetIgnoredArea(null);
        recorder.Simulate(Win32.WM_LBUTTONUP, Mouse(10, 10));
        Assert.Equal(2, recorder.Count);
    }

    [Fact]
    public void Gancho_DeEmergencia_PausaParaUnaVez_YEscMantenidaConElTemporizador()
    {
        var stops = 0;
        using var fired = new SemaphoreSlim(0);
        using (var watcher = new EmergencyStopWatcher(EmergencyKeys.Pause, 1000, () => { Interlocked.Increment(ref stops); fired.Release(); }))
        {
            watcher.Simulate(Win32.WM_KEYDOWN, Kbd(0x41));
            watcher.Simulate(Win32.WM_KEYDOWN, Kbd(KeyNames.Pause));
            Assert.True(fired.Wait(5000));
            watcher.Simulate(Win32.WM_SYSKEYDOWN, Kbd(KeyNames.Pause));
            watcher.SimulateTimer();
        }
        Thread.Sleep(50);
        Assert.Equal(1, stops);

        using var escaped = new SemaphoreSlim(0);
        using var esc = new EmergencyStopWatcher(EmergencyKeys.EscapeHold, 200, () => escaped.Release());
        esc.Simulate(Win32.WM_KEYDOWN, Kbd(KeyNames.Escape));
        esc.SimulateTimer();   // aun no
        Assert.False(escaped.Wait(0));
        Thread.Sleep(250);
        esc.SimulateTimer();
        Assert.True(escaped.Wait(5000));
    }

    [Fact]
    public void Ganchos_DosVecesEnMarcha_NoSePuede()
    {
        using var watcher = new EmergencyStopWatcher(EmergencyKeys.All, 1000, () => { });
        watcher.Start();
        Assert.True(watcher.IsRunning);
        Assert.Throws<InvalidOperationException>(watcher.Start);
        watcher.Stop();
        Assert.False(watcher.IsRunning);
        watcher.Stop();   // ya parado: nada
    }

    [Fact]
    public void Elevacion_YTeclado_SePuedenPreguntar()
    {
        Assert.False(Elevation.IsWindowElevated(0));
        _ = Elevation.IsCurrentProcessElevated();
        _ = Elevation.ForegroundIsElevatedAndWeAreNot();
        _ = new SystemKeyboardState().AnyModifierDown();
        var (x, y) = Screens.Cursor();
        Assert.True(Screens.Virtual().Contains(x, y) || Screens.Virtual().IsEmpty);
    }

    [Fact]
    public void Plataforma_DeVerdad_CreaLasPiezasDeVerdad()
    {
        var real = new RealPlatform();
        using (var recorder = real.CreateRecorder(new RecorderOptions()))
            Assert.IsType<Recorder>(recorder);
        Assert.IsType<Win32InputSink>(real.CreateSink());
        Assert.IsType<StopwatchClock>(real.CreateClock());
        Assert.IsType<SystemKeyboardState>(real.Keyboard);
        using (var e = real.CreateEmergency(EmergencyKeys.All, 1000, () => { }))
            Assert.IsType<EmergencyStopWatcher>(e);
        Assert.Equal(Elevation.IsCurrentProcessElevated(), real.IsElevated);
        _ = real.ForegroundIsElevatedAndWeAreNot();
        Assert.Equal(Microsoft.Win32.Registry.CurrentUser.Name, real.AssociationRoot.Name);
        Assert.Equal(Environment.ProcessPath, real.ExePath);
        Assert.False(real.IsPackaged);
        using var instance = real.CreateInstance("1.0");
        Assert.NotNull(instance);
    }

    // ------------------------------------------------------------ reproductor de los exe

    private sealed class Host
    {
        public List<(string Text, string Caption)> Errors { get; } = [];
        public StringWriter Out { get; } = new();
        public RecordingSink Sink { get; } = new();
        public FakeEmergency Emergency { get; } = new();
        public Action? OnEmergencyStart { get; set; }

        public PlayerHost Build() => new(
            (t, c) => Errors.Add((t, c)),
            Out,
            () => Sink,
            () => new FakeClock(),
            new NoKeys(),
            (keys, ms, stop) =>
            {
                OnEmergencyStart = stop;
                return Emergency;
            });

        private sealed class NoKeys : IKeyboardState
        {
            public bool AnyModifierDown() => false;
        }
    }

    private static string Compiled(TempFolder dir, ExeOptions options, Recording? recording = null)
    {
        var exe = dir.File("macro.exe");
        ExeBuilder.Build(new MemoryStream(ExeTests.FakePlayer()), recording ?? new Recording(MainWindowTests.Events()), options, exe);
        return exe;
    }

    [Fact]
    public void Reproductor_Check_ValidaSinReproducir()
    {
        using var dir = new TempFolder();
        var host = new Host();
        var exe = Compiled(dir, new ExeOptions());
        Assert.Equal(0, Program.Run(["--CHECK"], exe, host.Build()));
        Assert.StartsWith("OK 5 140 1 Once 1", host.Out.ToString());
        Assert.Empty(host.Sink.Sent);
    }

    [Fact]
    public void Reproductor_Reproduce_ConParadaDeEmergencia()
    {
        using var dir = new TempFolder();
        var host = new Host();
        var exe = Compiled(dir, new ExeOptions { Emergency = EmergencyKeys.All });
        Assert.Equal(0, Program.Run([], exe, host.Build()));
        Assert.Equal(5, host.Sink.Sent.Count);
        Assert.True(host.Emergency.Started);
        Assert.True(host.Emergency.Disposed);
    }

    [Fact]
    public void Reproductor_SinParadaDeEmergencia_NoLaPone_YSiSeParaDevuelveUno()
    {
        using var dir = new TempFolder();
        var host = new Host();
        var exe = Compiled(dir, new ExeOptions { Emergency = EmergencyKeys.None });
        Assert.Equal(0, Program.Run([], exe, host.Build()));
        Assert.False(host.Emergency.Started);

        var stopping = new Host();
        stopping.Sink.OnSend = _ => stopping.OnEmergencyStart!();
        Assert.Equal(1, Program.Run([], Compiled(dir, new ExeOptions { Emergency = EmergencyKeys.Pause }), stopping.Build()));
    }

    [Fact]
    public void Reproductor_SiNoSePuedePonerElGancho_Avisa()
    {
        using var dir = new TempFolder();
        var host = new Host();
        host.Emergency.StartError = new System.ComponentModel.Win32Exception(5);
        Assert.Equal(5, Program.Run([], Compiled(dir, new ExeOptions { Emergency = EmergencyKeys.All }), host.Build()));
        Assert.Equal(SocAutoTask.Localization.Loc.Get("PlayerHookFailed"), Assert.Single(host.Errors).Text);
        Assert.Empty(host.Sink.Sent);
    }

    [Fact]
    public void Reproductor_SinGrabacion_OConLaGrabacionDañada()
    {
        using var dir = new TempFolder();
        var host = new Host();
        Assert.Equal(2, Program.Run([], null, host.Build()));

        var plain = dir.File("plain.exe");
        File.WriteAllBytes(plain, ExeTests.FakePlayer());
        Assert.Equal(3, Program.Run(["--check"], plain, host.Build()));
        Assert.Empty(host.Errors);
        Assert.Equal(3, Program.Run([], plain, host.Build()));
        Assert.Equal(SocAutoTask.Localization.Loc.Get("PlayerNoRecording"), Assert.Single(host.Errors).Text);

        var exe = Compiled(dir, new ExeOptions());
        var bytes = File.ReadAllBytes(exe);
        bytes[4096 + 20] ^= 0xFF;   // dentro de la grabacion
        File.WriteAllBytes(exe, bytes);
        Assert.Equal(4, Program.Run(["--check"], exe, host.Build()));
        Assert.Equal(4, Program.Run([], exe, host.Build()));
        Assert.Equal(2, host.Errors.Count);

        Assert.Equal(4, Program.Run([], dir.File("no-existe.exe"), host.Build()));
    }

    [Fact]
    public void Reproductor_DeVerdad_LaPiezaReal()
    {
        Assert.NotNull(PlayerHost.Real.Sink());
        Assert.IsType<StopwatchClock>(PlayerHost.Real.Clock());
        using var e = PlayerHost.Real.Emergency(EmergencyKeys.All, 1000, () => { });
        Assert.IsType<EmergencyStopWatcher>(e);
    }
}
