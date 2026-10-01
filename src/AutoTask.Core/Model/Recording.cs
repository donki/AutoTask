namespace SocAutoTask.Model;

/// <summary>Como se repite una grabacion (RF-20). El valor numerico va al fichero.</summary>
public enum RepeatMode : byte
{
    Once = 0,
    Times = 1,
    Continuous = 2,
}

/// <summary>Velocidad y repeticiones: viajan con la grabacion (RF-21).</summary>
public sealed record PlaybackOptions
{
    public const double MinSpeed = 0.1;
    public const double MaxSpeed = 1000;
    public const int MaxTimes = 100_000;
    public const int MaxPauseMs = 3_600_000;

    /// <summary>Las velocidades del desplegable (RF-19); cualquier otra es «personalizada».</summary>
    public static readonly double[] PresetSpeeds = [0.5, 1, 2, 4, 10, 100];

    public double Speed { get; init; } = 1;
    public RepeatMode Repeat { get; init; } = RepeatMode.Once;
    public int Times { get; init; } = 1;
    public int PauseBetweenMs { get; init; }

    /// <summary>Vueltas que se daran: null si es continuo.</summary>
    public int? Loops => Repeat switch
    {
        RepeatMode.Once => 1,
        RepeatMode.Times => Math.Max(1, Times),
        _ => null,
    };

    /// <summary>La misma con los valores llevados a sus limites (lo que venga de un fichero o de la interfaz).</summary>
    public PlaybackOptions Normalized() => this with
    {
        Speed = double.IsFinite(Speed) ? Math.Clamp(Speed, MinSpeed, MaxSpeed) : 1,
        Times = Math.Clamp(Times, 1, MaxTimes),
        PauseBetweenMs = Math.Clamp(PauseBetweenMs, 0, MaxPauseMs),
        Repeat = Enum.IsDefined(Repeat) ? Repeat : RepeatMode.Once,
    };
}

/// <summary>Rectangulo de la pantalla virtual (todos los monitores) en pixeles fisicos.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>El punto llevado al borde si cae fuera (monitor desconectado desde que se grabo).</summary>
    public (int X, int Y) Clamp(int x, int y) =>
        IsEmpty ? (x, y) : (Math.Clamp(x, Left, Right - 1), Math.Clamp(y, Top, Bottom - 1));
}

/// <summary>Una grabacion: eventos, opciones de reproduccion y con que pantalla se hizo.</summary>
public sealed class Recording
{
    public Recording(IEnumerable<MacroEvent> events, PlaybackOptions? options = null, ScreenRect screen = default, DateTime? recordedUtc = null)
    {
        Events = events as List<MacroEvent> ?? [.. events];
        Options = (options ?? new PlaybackOptions()).Normalized();
        Screen = screen;
        RecordedUtc = recordedUtc ?? DateTime.UtcNow;
    }

    public List<MacroEvent> Events { get; }

    public PlaybackOptions Options { get; set; }

    public ScreenRect Screen { get; }

    public DateTime RecordedUtc { get; }

    public int Count => Events.Count;

    public bool IsEmpty => Events.Count == 0;

    /// <summary>Duracion de una vuelta a 1×, en milisegundos.</summary>
    public long TotalMs
    {
        get
        {
            long total = 0;
            foreach (var e in Events)
                total += Math.Max(0, e.DelayMs);
            return total;
        }
    }

    public Recording WithEvents(IEnumerable<MacroEvent> events) => new(events, Options, Screen, RecordedUtc);
}
