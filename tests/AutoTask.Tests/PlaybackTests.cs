using SocAutoTask.Input;
using SocAutoTask.Model;
using SocAutoTask.Playback;

namespace SocAutoTask.Tests;

/// <summary>Reproduccion con reloj y SendInput falsos (RF-11..20, CA-04, CA-05).</summary>
public sealed class PlaybackTests
{
    private static List<MacroEvent> Second() =>
    [
        MacroEvent.Move(0, 0),
        MacroEvent.Move(10, 10, 250),
        MacroEvent.Down(MouseButton.Left, 10, 10, 250),
        MacroEvent.Up(MouseButton.Left, 10, 10, 250),
        MacroEvent.Key(true, 0x41, delay: 250),   // total 1000 ms
    ];

    [Fact]
    public void Plan_InstantesAbsolutos_SegunLaVelocidad()
    {
        var plan = PlaybackPlan.Build(Second(), 2);
        Assert.Equal([0, 125, 250, 375, 500], plan.Offsets);
        Assert.Equal(500, plan.DurationMs);
        Assert.Equal(500 * 3 + 2 * 200, plan.TotalMs(new PlaybackOptions { Repeat = RepeatMode.Times, Times = 3, PauseBetweenMs = 200 }));
        Assert.Null(plan.TotalMs(new PlaybackOptions { Repeat = RepeatMode.Continuous }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackPlan.Build(Second(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackPlan.Build(Second(), double.NaN));
    }

    [Fact]
    public void A2x_UnaGrabacionDeUnSegundo_DuraMedio()
    {
        // CA-04
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        var session = new PlaybackSession(Second(), new PlaybackOptions { Speed = 2 }, sink, clock);
        Assert.Equal(StopReason.None, session.Run());
        Assert.Equal([0, 125, 250, 375, 500], sink.Sent.Take(5).Select(s => s.At));
        Assert.Equal(500, clock.NowMs);
        Assert.Equal(1, clock.HighResolutionCount);
    }

    [Theory]
    [InlineData(0.5, 2000)]
    [InlineData(1, 1000)]
    [InlineData(4, 250)]
    [InlineData(10, 100)]
    [InlineData(100, 10)]
    [InlineData(1000, 1)]
    public void Velocidades(double speed, double expected)
    {
        var clock = new FakeClock();
        new PlaybackSession(Second(), new PlaybackOptions { Speed = speed }, new RecordingSink(clock), clock).Run();
        Assert.Equal(expected, clock.NowMs, 6);
    }

    [Fact]
    public void TresVueltas_ConPausa_DuranLoQueDeben()
    {
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        var options = new PlaybackOptions { Repeat = RepeatMode.Times, Times = 3, PauseBetweenMs = 200 };
        var session = new PlaybackSession(Second(), options, sink, clock);
        Assert.Equal(StopReason.None, session.Run());
        Assert.Equal(3 * 1000 + 2 * 200, clock.NowMs);
        // 5 eventos por vuelta + al final se suelta la A que se quedo pulsada.
        Assert.Equal(16, sink.Sent.Count);
        Assert.Equal(15, session.Sent);
        Assert.Equal(1200, sink.Sent[5].At);   // la segunda vuelta empieza tras 1000 + 200
    }

    [Fact]
    public void Continuo_HastaQueSePara()
    {
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        var session = new PlaybackSession(Second(), new PlaybackOptions { Repeat = RepeatMode.Continuous }, sink, clock);
        sink.OnSend = _ =>
        {
            if (session.Sent >= 22)
                session.Control.Stop(StopReason.Hotkey);
        };
        Assert.Equal(StopReason.Hotkey, session.Run());
        Assert.Equal(23, session.Sent);
    }

    [Fact]
    public void ParadaDeEmergencia_SueltaTeclasYBotones()
    {
        // CA-05: se para con el boton y la tecla pulsados; al acabar se sueltan los dos.
        List<MacroEvent> events =
        [
            MacroEvent.Key(true, 0xA2, 0x1D),                  // Ctrl
            MacroEvent.Down(MouseButton.Left, 50, 60, 10),
            MacroEvent.Move(70, 80, 10),
            MacroEvent.Key(false, 0xA2, 0x1D, delay: 1000),
            MacroEvent.Up(MouseButton.Left, 70, 80, 10),
        ];
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        var session = new PlaybackSession(events, new PlaybackOptions(), sink, clock);
        clock.Advanced = t =>
        {
            if (t >= 500)
                session.Control.Stop(StopReason.Emergency);
        };
        Assert.Equal(StopReason.Emergency, session.Run());
        var sent = sink.Sent.Select(s => s.Event).ToList();
        Assert.Equal(5, sent.Count);
        Assert.Equal(MacroEvent.Up(MouseButton.Left, 70, 80), sent[3]);
        Assert.Equal(EventKind.KeyUp, sent[4].Kind);
        Assert.Equal(0xA2, sent[4].VirtualKey);
        Assert.Equal(0x1D, sent[4].ScanCode);
    }

    [Fact]
    public void Espera_NoSeEnvia_PeroCuenta()
    {
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        new PlaybackSession([MacroEvent.Move(0, 0), MacroEvent.WaitFor(3000), MacroEvent.Move(1, 1)], new PlaybackOptions(), sink, clock).Run();
        Assert.Equal(2, sink.Sent.Count);
        Assert.Equal(3000, sink.Sent[1].At);
    }

    [Fact]
    public void EsperaAQueSeSuelten_LosModificadores_YCuentaAtras()
    {
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        var keyboard = new FakeKeyboard(clock, releaseAt: 300);
        var statuses = new List<PlaybackStatus>();
        var session = new PlaybackSession(Second(), new PlaybackOptions(), sink, clock, keyboard, countdownSeconds: 2);
        session.Status += statuses.Add;
        session.Run();
        Assert.Equal(PlaybackPhase.WaitingForKeys, statuses[0].Phase);
        Assert.Equal([2, 1], statuses.Where(s => s.Phase == PlaybackPhase.Countdown).Select(s => s.CountdownSeconds));
        // Soltadas a los 300 ms (+ 50 de respiro, redondeado a la vuelta de 20) y 2 s de cuenta atras.
        Assert.InRange(sink.Sent[0].At, 2350, 2380);
        Assert.Equal(PlaybackPhase.Finished, statuses[^1].Phase);
    }

    [Fact]
    public void ModificadoresQueNoSeSueltan_EmpiezaIgualTrasDiezSegundos()
    {
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        new PlaybackSession(Second(), new PlaybackOptions(), sink, clock, new FakeKeyboard(clock, double.MaxValue)).Run();
        Assert.InRange(sink.Sent[0].At, PlaybackSession.MaxWaitForKeysMs, PlaybackSession.MaxWaitForKeysMs + 100);
    }

    [Fact]
    public void PararDuranteLaCuentaAtras_NoEnviaNada()
    {
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        var session = new PlaybackSession(Second(), new PlaybackOptions(), sink, clock, countdownSeconds: 3);
        clock.Advanced = t => { if (t >= 1000) session.Control.Stop(StopReason.User); };
        Assert.Equal(StopReason.User, session.Run());
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void PararEnLaPausaEntreVueltas()
    {
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        var session = new PlaybackSession(Second(), new PlaybackOptions { Repeat = RepeatMode.Times, Times = 5, PauseBetweenMs = 10_000 }, sink, clock);
        clock.Advanced = t => { if (t > 1500) session.Control.Stop(StopReason.Closing); };
        Assert.Equal(StopReason.Closing, session.Run());
        Assert.Equal(5, session.Sent);
    }

    [Fact]
    public void Progreso_DiceVueltaYLoQueQueda()
    {
        var events = Enumerable.Range(0, 100).Select(i => MacroEvent.Move(i, i, i == 0 ? 0 : 10)).ToList();   // 990 ms
        var clock = new FakeClock();
        var statuses = new List<PlaybackStatus>();
        var session = new PlaybackSession(events, new PlaybackOptions { Repeat = RepeatMode.Times, Times = 2 }, new RecordingSink(clock), clock);
        session.Status += statuses.Add;
        session.Run();
        var playing = statuses.Where(s => s.Phase == PlaybackPhase.Playing).ToList();
        Assert.InRange(playing.Count, 18, 24);   // cada 100 ms, no por cada evento
        Assert.Equal(1, playing[0].Loop);
        Assert.Equal(2, playing[0].Loops);
        Assert.Equal(1980, playing[0].RemainingTotalMs);
        Assert.Equal(2, playing[^1].Loop);
        Assert.True(playing.Zip(playing.Skip(1)).All(p => p.Second.RemainingTotalMs <= p.First.RemainingTotalMs));
    }

    [Fact]
    public void Progreso_Continuo_SinTotal()
    {
        var clock = new FakeClock();
        var statuses = new List<PlaybackStatus>();
        var session = new PlaybackSession(Second(), new PlaybackOptions { Repeat = RepeatMode.Continuous }, new RecordingSink(clock), clock);
        session.Status += s =>
        {
            statuses.Add(s);
            if (s.Loop == 3)
                session.Control.Stop(StopReason.User);
        };
        session.Run();
        Assert.All(statuses.Where(s => s.Phase == PlaybackPhase.Playing), s => Assert.Null(s.RemainingTotalMs));
        Assert.All(statuses.Where(s => s.Phase == PlaybackPhase.Playing), s => Assert.Null(s.Loops));
    }

    [Fact]
    public void QuienEscuchaFalla_LaReproduccionSigue()
    {
        var clock = new FakeClock();
        var sink = new RecordingSink(clock);
        var session = new PlaybackSession(Second(), new PlaybackOptions(), sink, clock);
        session.Status += _ => throw new InvalidOperationException("la interfaz se ha caido");
        Assert.Equal(StopReason.None, session.Run());
        Assert.Equal(5, session.Sent);
    }

    [Fact]
    public void ContinuoSinEventos_AcabaEnseguida()
    {
        var clock = new FakeClock();
        Assert.Equal(StopReason.None, new PlaybackSession([], new PlaybackOptions { Repeat = RepeatMode.Continuous }, new RecordingSink(clock), clock).Run());
    }

    [Fact]
    public void EnUnHilo_AvisaAlAcabar()
    {
        var clock = new FakeClock();
        var session = new PlaybackSession(Second(), new PlaybackOptions(), new RecordingSink(clock), clock);
        StopReason? result = null;
        var thread = session.Start(r => result = r);
        Assert.True(thread.Join(5000));
        Assert.Equal(StopReason.None, result);
    }

    [Fact]
    public void Control_SoloCuentaLaPrimeraRazon()
    {
        using var control = new PlaybackControl();
        Assert.False(control.IsStopRequested);
        control.Stop(StopReason.Emergency);
        control.Stop(StopReason.User);
        Assert.Equal(StopReason.Emergency, control.Reason);
        Assert.True(control.Handle.WaitOne(0));
    }

    [Fact]
    public void RelojDeVerdad_EsperaYSeDespiertaAlParar()
    {
        var clock = new StopwatchClock();
        using var control = new PlaybackControl();
        using (clock.HighResolution())
        {
            var start = clock.NowMs;
            Assert.True(clock.WaitUntil(start + 30, control));
            Assert.InRange(clock.NowMs - start, 30, 200);
            Assert.True(clock.WaitUntil(start - 5, control));   // ya paso
            var t = new Thread(() => { Thread.Sleep(50); control.Stop(); });
            t.Start();
            var before = clock.NowMs;
            Assert.False(clock.WaitUntil(before + 60_000, control));
            Assert.InRange(clock.NowMs - before, 0, 2000);
            t.Join();
        }
    }

    [Fact]
    public void Pulsados_SeSueltanEnOrden()
    {
        var tracker = new PressedTracker();
        tracker.Track(MacroEvent.Key(true, 0x10));
        tracker.Track(MacroEvent.Key(true, 0x41));
        tracker.Track(MacroEvent.Down(MouseButton.Right, 5, 6));
        tracker.Track(MacroEvent.Move(9, 9));
        tracker.Track(MacroEvent.Key(false, 0x41));
        tracker.Track(MacroEvent.WheelAt(7, 7, 120));
        Assert.Equal(2, tracker.Count);
        var releases = tracker.Releases();
        Assert.Equal(MacroEvent.Up(MouseButton.Right, 7, 7), releases[0]);
        Assert.Equal(0x10, releases[1].VirtualKey);
        Assert.Equal(EventKind.KeyUp, releases[1].Kind);
        Assert.Equal(0, tracker.Count);
        Assert.Empty(tracker.Releases());
    }

    [Fact]
    public void Opciones_SeNormalizan()
    {
        var o = new PlaybackOptions { Speed = 5000, Times = 0, PauseBetweenMs = -3, Repeat = (RepeatMode)9 }.Normalized();
        Assert.Equal(PlaybackOptions.MaxSpeed, o.Speed);
        Assert.Equal(1, o.Times);
        Assert.Equal(0, o.PauseBetweenMs);
        Assert.Equal(RepeatMode.Once, o.Repeat);
        Assert.Equal(1, new PlaybackOptions { Speed = double.NaN }.Normalized().Speed);
        Assert.Equal(3, new PlaybackOptions { Repeat = RepeatMode.Times, Times = 3 }.Loops);
        Assert.Null(new PlaybackOptions { Repeat = RepeatMode.Continuous }.Loops);
    }

    [Fact]
    public void Pantalla_ContieneYLlevaAlBorde()
    {
        var screen = new ScreenRect(-1920, 0, 3840, 1080);
        Assert.True(screen.Contains(-1920, 0));
        Assert.False(screen.Contains(1920, 0));
        Assert.Equal((1919, 1079), screen.Clamp(5000, 5000));
        Assert.Equal((-1920, 0), screen.Clamp(-9999, -5));
        Assert.Equal((5, 5), default(ScreenRect).Clamp(5, 5));
        Assert.True(default(ScreenRect).IsEmpty);
    }

    [Fact]
    public void Evento_Propiedades()
    {
        var key = MacroEvent.Key(true, 0x25, 0x4B, extended: true);
        Assert.True(key.IsKey);
        Assert.False(key.IsMouse);
        Assert.True(key.IsExtended);
        Assert.True(MacroEvent.WheelAt(1, 1, 120).IsMouse);
        Assert.False(MacroEvent.WaitFor(5).IsMouse);
        var recording = new Recording([MacroEvent.Move(0, 0, 5), MacroEvent.Move(0, 0, -3)]);
        Assert.Equal(5, recording.TotalMs);
        Assert.Equal(recording.Options, recording.WithEvents([]).Options);
    }
}

/// <summary>Parada de emergencia y coordenadas (RF-04, RF-13, CA-02).</summary>
public sealed class InputTests
{
    [Fact]
    public void Pausa_YBloqDespl_ParanAlPulsar()
    {
        var logic = new EmergencyStopLogic(EmergencyKeys.All, 1000);
        Assert.True(logic.OnKey(KeyNames.Pause, true, false, 0));
        Assert.True(logic.OnKey(KeyNames.ScrollLock, true, false, 0));
        Assert.False(logic.OnKey(KeyNames.Pause, false, false, 0));
        Assert.False(logic.OnKey(0x41, true, false, 0));
    }

    [Fact]
    public void Inyectadas_NoCuentan()
    {
        var logic = new EmergencyStopLogic(EmergencyKeys.All, 1000);
        Assert.False(logic.OnKey(KeyNames.Pause, true, true, 0));
        Assert.False(logic.OnKey(KeyNames.Escape, true, true, 0));
        Assert.False(logic.OnKey(KeyNames.Escape, true, true, 5000));
    }

    [Fact]
    public void EscMantenida_ConRepeticionesOConElTemporizador()
    {
        var logic = new EmergencyStopLogic(EmergencyKeys.EscapeHold, 1000);
        Assert.False(logic.OnKey(KeyNames.Escape, true, false, 100));
        Assert.False(logic.OnKey(KeyNames.Escape, true, false, 700));
        Assert.True(logic.OnKey(KeyNames.Escape, true, false, 1100));

        var timer = new EmergencyStopLogic(EmergencyKeys.EscapeHold, 1000);
        timer.OnKey(KeyNames.Escape, true, false, 0);
        Assert.False(timer.OnTick(999));
        Assert.True(timer.OnTick(1000));
    }

    [Fact]
    public void EscSoltadaAntes_NoPara()
    {
        var logic = new EmergencyStopLogic(EmergencyKeys.EscapeHold, 1000);
        logic.OnKey(KeyNames.Escape, true, false, 0);
        logic.OnKey(KeyNames.Escape, false, false, 500);
        Assert.False(logic.OnTick(2000));
        Assert.False(logic.OnKey(KeyNames.Escape, true, false, 2100));
    }

    [Fact]
    public void TeclasDesactivadas_NoParan()
    {
        var logic = new EmergencyStopLogic(EmergencyKeys.None, 1000);
        Assert.False(logic.OnKey(KeyNames.Pause, true, false, 0));
        Assert.False(logic.OnKey(KeyNames.ScrollLock, true, false, 0));
        Assert.False(logic.OnKey(KeyNames.Escape, true, false, 0));
        Assert.False(logic.OnKey(KeyNames.Escape, true, false, 99999));
    }

    [Theory]
    [InlineData(10, 200)]
    [InlineData(99999, 10000)]
    public void TiempoDeEsc_SeLimita(int asked, int expected) => Assert.Equal(expected, new EmergencyStopLogic(EmergencyKeys.All, asked).EscapeHoldMs);

    [Theory]
    [InlineData(-1920, 3840)]   // dos monitores, el de la izquierda en negativo (el equipo de Josep)
    [InlineData(0, 1920)]
    [InlineData(0, 2559)]
    [InlineData(-1280, 4480)]
    [InlineData(0, 1080)]
    [InlineData(-300, 1501)]
    public void Coordenadas_CadaPixel_VuelveAlMismo(int origin, int size)
    {
        // CA-02: lo que se envia a SendInput cae en el mismo pixel, en toda la pantalla.
        for (var x = origin; x < origin + size; x++)
        {
            var n = CoordinateMapper.Normalize(x, origin, size);
            Assert.InRange(n, 0, 65535);
            Assert.Equal(x, CoordinateMapper.Denormalize(n, origin, size));
        }
    }

    [Fact]
    public void Coordenadas_FueraDePantalla_AlBorde()
    {
        Assert.Equal(0, CoordinateMapper.Normalize(-5000, -1920, 3840));
        Assert.Equal(1919, CoordinateMapper.Denormalize(CoordinateMapper.Normalize(99999, -1920, 3840), -1920, 3840));
        Assert.Equal(0, CoordinateMapper.Normalize(5, 0, 1));
    }

    [Fact]
    public void Atajos_PorDefecto()
    {
        Assert.Equal("Ctrl+Alt+Shift+R", Hotkey.DefaultRecord.ToString());
        Assert.Equal("Ctrl+Alt+Shift+P", Hotkey.DefaultPlay.ToString());
        Assert.True(Hotkey.DefaultRecord.IsValid);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Shift+R", "Ctrl+Alt+Shift+R")]
    [InlineData("shift + ctrl + f9", "Ctrl+Shift+F9")]
    [InlineData("Win+Pause", "Win+Pause")]
    [InlineData("Alt+NumPad5", "Alt+NumPad5")]
    [InlineData("Ctrl+VK07", "Ctrl+VK07")]
    [InlineData("Control+Mayús+Space", "Ctrl+Shift+Space")]
    public void Atajos_SeLeenYSeEscriben(string text, string expected)
    {
        Assert.True(Hotkey.TryParse(text, out var h));
        Assert.Equal(expected, h.ToString());
        Assert.True(Hotkey.TryParse(h.ToString(), out var again));
        Assert.Equal(h, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("R")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+R+P")]
    [InlineData("Ctrl+Patata")]
    [InlineData("Ctrl+VKZZ")]
    [InlineData("Ctrl+LShift")]
    public void Atajos_NoValidos(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
        Assert.Equal(Hotkey.DefaultPlay, Hotkey.ParseOr(text, Hotkey.DefaultPlay));
    }

    [Fact]
    public void Atajos_SeEnseñanEnElIdioma()
    {
        try
        {
            Localization.Loc.Use("es");
            Assert.Equal("Ctrl+Alt+Mayús+R", Hotkey.DefaultRecord.Display());
            Localization.Loc.Use("en");
            Assert.Equal("Ctrl+Alt+Shift+R", Hotkey.DefaultRecord.Display());
        }
        finally
        {
            Localization.Loc.Use("en");
        }
    }

    [Fact]
    public void NombresDeTeclas_UnicosYDeIdaYVuelta()
    {
        for (ushort vk = 1; vk < 0xFF; vk++)
        {
            var name = KeyNames.Name(vk);
            Assert.True(KeyNames.TryParse(name, out var back), name);
            Assert.Equal(vk, back);
        }
        Assert.True(KeyNames.IsModifier(KeyNames.LShift));
        Assert.False(KeyNames.IsModifier(0x41));
    }
}
