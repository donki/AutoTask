using SocAutoTask.Model;

namespace SocAutoTask.Playback;

/// <summary>
/// Cuando cae cada evento de una vuelta a una velocidad dada (RF-11): instantes absolutos desde el
/// principio de la vuelta, para que los retrasos del sistema no se acumulen.
/// </summary>
public sealed class PlaybackPlan
{
    private PlaybackPlan(double[] offsets, double duration)
    {
        Offsets = offsets;
        DurationMs = duration;
    }

    /// <summary>Instante de cada evento desde el inicio de la vuelta (ms).</summary>
    public double[] Offsets { get; }

    /// <summary>Lo que dura una vuelta (ms) a esa velocidad.</summary>
    public double DurationMs { get; }

    public static PlaybackPlan Build(IReadOnlyList<MacroEvent> events, double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed));
        var offsets = new double[events.Count];
        long sum = 0;
        for (var i = 0; i < events.Count; i++)
        {
            sum += Math.Max(0, events[i].DelayMs);
            offsets[i] = sum / speed;
        }
        return new PlaybackPlan(offsets, sum / speed);
    }

    /// <summary>
    /// Lo que dura todo (ms) con las vueltas y las pausas, o null si es continuo.
    /// </summary>
    public double? TotalMs(PlaybackOptions options) => options.Loops is { } loops
        ? loops * DurationMs + (loops - 1) * (double)Math.Max(0, options.PauseBetweenMs)
        : null;
}

/// <summary>En que esta la reproduccion, para enseñarlo (RF-16, RF-17).</summary>
public enum PlaybackPhase
{
    WaitingForKeys,
    Countdown,
    Playing,
    PauseBetweenLoops,
    Finished,
}

/// <summary>Una foto del progreso.</summary>
public sealed record PlaybackStatus
{
    public PlaybackPhase Phase { get; init; }
    public int CountdownSeconds { get; init; }
    /// <summary>Vuelta actual, desde 1.</summary>
    public int Loop { get; init; }
    /// <summary>Vueltas en total; null si es continuo.</summary>
    public int? Loops { get; init; }
    public int EventIndex { get; init; }
    public double RemainingLoopMs { get; init; }
    /// <summary>Lo que falta en total; null si es continuo.</summary>
    public double? RemainingTotalMs { get; init; }
}

/// <summary>
/// Teclas y botones que la reproduccion ha dejado pulsados, para soltarlos al acabar o al parar
/// (RF-14): nunca queda un Ctrl o un clic enganchado.
/// </summary>
public sealed class PressedTracker
{
    private readonly Dictionary<ushort, MacroEvent> _keys = [];
    private readonly Dictionary<MouseButton, MacroEvent> _buttons = [];
    private (int X, int Y) _lastMouse;

    public int Count => _keys.Count + _buttons.Count;

    public void Track(in MacroEvent e)
    {
        switch (e.Kind)
        {
            case EventKind.KeyDown: _keys[e.VirtualKey] = e; break;
            case EventKind.KeyUp: _keys.Remove(e.VirtualKey); break;
            case EventKind.MouseDown: _buttons[e.Button] = e; _lastMouse = (e.X, e.Y); break;
            case EventKind.MouseUp: _buttons.Remove(e.Button); _lastMouse = (e.X, e.Y); break;
            case EventKind.MouseMove or EventKind.Wheel or EventKind.HWheel: _lastMouse = (e.X, e.Y); break;
        }
    }

    /// <summary>Las sueltas que faltan (primero los botones, luego las teclas, en orden inverso).</summary>
    public List<MacroEvent> Releases()
    {
        var result = new List<MacroEvent>();
        foreach (var b in _buttons.Keys)
            result.Add(MacroEvent.Up(b, _lastMouse.X, _lastMouse.Y));
        foreach (var k in _keys.Values.Reverse())
            result.Add(k with { Kind = EventKind.KeyUp, DelayMs = 0 });
        _buttons.Clear();
        _keys.Clear();
        return result;
    }
}

/// <summary>
/// Reproduce una grabacion (RF-11..17, RF-20): espera a que se suelten los modificadores, cuenta
/// atras, vueltas con pausa, progreso, y suelta lo pulsado al acabar. Bloquea: se lanza en un hilo
/// propio (<see cref="Start"/>).
/// </summary>
public sealed class PlaybackSession(IReadOnlyList<MacroEvent> events, PlaybackOptions options, IInputSink sink, IClock clock, IKeyboardState? keyboard = null, int countdownSeconds = 0)
{
    /// <summary>Cada cuanto (ms de reloj) se avisa del progreso como mucho.</summary>
    public const double ProgressEveryMs = 100;

    /// <summary>Lo maximo que se espera a que se suelten los modificadores antes de empezar igual.</summary>
    public const double MaxWaitForKeysMs = 10_000;

    private readonly PlaybackOptions _options = options.Normalized();

    public PlaybackControl Control { get; } = new();

    /// <summary>Progreso. Llega desde el hilo de la reproduccion.</summary>
    public event Action<PlaybackStatus>? Status;

    /// <summary>Eventos enviados (para las pruebas y el registro).</summary>
    public long Sent { get; private set; }

    public Thread Start(Action<StopReason>? finished = null)
    {
        var thread = new Thread(() =>
        {
            var reason = Run();
            finished?.Invoke(reason);
        })
        {
            IsBackground = true,
            Name = "AutoTask playback",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
        return thread;
    }

    /// <summary>Reproduce entero (o hasta que se pare). Devuelve por que acabo: None si llego al final.</summary>
    public StopReason Run()
    {
        var tracker = new PressedTracker();
        using var resolution = clock.HighResolution();
        try
        {
            if (!WaitForModifiers() || !Countdown())
                return Control.Reason;

            var plan = PlaybackPlan.Build(events, _options.Speed);
            var loops = _options.Loops;
            var total = plan.TotalMs(_options);
            var start = clock.NowMs;
            var lastReport = double.NegativeInfinity;

            for (var loop = 0; loops is null || loop < loops; loop++)
            {
                if (loop > 0 && _options.PauseBetweenMs > 0)
                {
                    Report(new PlaybackStatus
                    {
                        Phase = PlaybackPhase.PauseBetweenLoops, Loop = loop + 1, Loops = loops,
                        RemainingTotalMs = total is { } t1 ? Math.Max(0, t1 - (clock.NowMs - start)) : null,
                    });
                    if (!clock.WaitUntil(clock.NowMs + _options.PauseBetweenMs, Control))
                        return Control.Reason;
                }

                var loopStart = clock.NowMs;
                for (var i = 0; i < events.Count; i++)
                {
                    var target = loopStart + plan.Offsets[i];
                    if (!clock.WaitUntil(target, Control))
                        return Control.Reason;
                    var e = events[i];
                    if (e.Kind != EventKind.Wait)
                    {
                        sink.Send(e);
                        tracker.Track(e);
                        Sent++;
                    }
                    var now = clock.NowMs;
                    if (now - lastReport >= ProgressEveryMs || i == 0)
                    {
                        lastReport = now;
                        Report(new PlaybackStatus
                        {
                            Phase = PlaybackPhase.Playing, Loop = loop + 1, Loops = loops, EventIndex = i,
                            RemainingLoopMs = Math.Max(0, plan.DurationMs - (now - loopStart)),
                            RemainingTotalMs = total is { } t2 ? Math.Max(0, t2 - (now - start)) : null,
                        });
                    }
                }
                if (Control.IsStopRequested)
                    return Control.Reason;
                if (events.Count == 0 && loops is null)
                    return StopReason.None;   // continuo sin eventos: no hay nada que repetir
            }
            return StopReason.None;
        }
        finally
        {
            foreach (var release in tracker.Releases())
                sink.Send(release);
            Report(new PlaybackStatus { Phase = PlaybackPhase.Finished });
        }
    }

    private bool WaitForModifiers()
    {
        if (keyboard is null || !keyboard.AnyModifierDown())
            return true;
        Report(new PlaybackStatus { Phase = PlaybackPhase.WaitingForKeys });
        var until = clock.NowMs + MaxWaitForKeysMs;
        while (keyboard.AnyModifierDown() && clock.NowMs < until)
        {
            if (!clock.WaitUntil(clock.NowMs + 20, Control))
                return false;
        }
        // Un respiro tras soltar: algunas aplicaciones procesan la suelta un instante despues.
        return clock.WaitUntil(clock.NowMs + 50, Control);
    }

    private bool Countdown()
    {
        for (var s = Math.Clamp(countdownSeconds, 0, 60); s > 0; s--)
        {
            Report(new PlaybackStatus { Phase = PlaybackPhase.Countdown, CountdownSeconds = s });
            if (!clock.WaitUntil(clock.NowMs + 1000, Control))
                return false;
        }
        return true;
    }

    private void Report(PlaybackStatus status)
    {
        try
        {
            Status?.Invoke(status);
        }
        catch (Exception)
        {
            // Quien escucha no puede tumbar la reproduccion (ni dejar teclas pulsadas).
        }
    }
}
