namespace SocAutoTask.Model;

/// <summary>Tipo de evento de una grabacion (ARQUITECTURA §3). El valor numerico va al fichero: no se renumera.</summary>
public enum EventKind : byte
{
    MouseMove = 0,
    MouseDown = 1,
    MouseUp = 2,
    Wheel = 3,
    HWheel = 4,
    KeyDown = 5,
    KeyUp = 6,
    /// <summary>Espera explicita insertada en el editor: solo cuenta su <see cref="MacroEvent.DelayMs"/>.</summary>
    Wait = 7,
}

/// <summary>Boton del raton. El valor numerico va al fichero.</summary>
public enum MouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2,
    X1 = 3,
    X2 = 4,
}

/// <summary>
/// Un evento grabado. Struct pequeño (24 bytes) para que una hora de grabacion quepa holgada en
/// memoria (RNF-05). Inmutable: el editor crea copias con <c>with</c>.
/// </summary>
public readonly record struct MacroEvent
{
    public const byte ExtendedFlag = 1;

    /// <summary>Que es.</summary>
    public EventKind Kind { get; init; }

    /// <summary>Boton (solo en MouseDown/MouseUp).</summary>
    public MouseButton Button { get; init; }

    /// <summary>Marcas: bit 0 = tecla extendida.</summary>
    public byte Flags { get; init; }

    /// <summary>Milisegundos desde el evento anterior (el primero, normalmente 0).</summary>
    public int DelayMs { get; init; }

    /// <summary>Coordenadas de escritorio virtual en pixeles fisicos (eventos de raton).</summary>
    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>Rueda: cantidad (±120 por muesca). Teclas: vk en los 16 bits bajos y scan en los altos.</summary>
    public int Data { get; init; }

    public bool IsMouse => Kind is EventKind.MouseMove or EventKind.MouseDown or EventKind.MouseUp or EventKind.Wheel or EventKind.HWheel;

    public bool IsKey => Kind is EventKind.KeyDown or EventKind.KeyUp;

    public bool IsExtended => (Flags & ExtendedFlag) != 0;

    /// <summary>Codigo virtual de la tecla.</summary>
    public ushort VirtualKey => (ushort)(Data & 0xFFFF);

    /// <summary>Codigo de exploracion de la tecla.</summary>
    public ushort ScanCode => (ushort)((Data >> 16) & 0xFFFF);

    public static MacroEvent Move(int x, int y, int delay = 0) => new() { Kind = EventKind.MouseMove, X = x, Y = y, DelayMs = delay };

    public static MacroEvent Down(MouseButton button, int x, int y, int delay = 0) => new() { Kind = EventKind.MouseDown, Button = button, X = x, Y = y, DelayMs = delay };

    public static MacroEvent Up(MouseButton button, int x, int y, int delay = 0) => new() { Kind = EventKind.MouseUp, Button = button, X = x, Y = y, DelayMs = delay };

    public static MacroEvent WheelAt(int x, int y, int amount, int delay = 0, bool horizontal = false) =>
        new() { Kind = horizontal ? EventKind.HWheel : EventKind.Wheel, X = x, Y = y, Data = amount, DelayMs = delay };

    public static MacroEvent Key(bool down, ushort vk, ushort scan = 0, bool extended = false, int delay = 0) => new()
    {
        Kind = down ? EventKind.KeyDown : EventKind.KeyUp,
        Data = vk | (scan << 16),
        Flags = extended ? ExtendedFlag : (byte)0,
        DelayMs = delay,
    };

    public static MacroEvent WaitFor(int ms) => new() { Kind = EventKind.Wait, DelayMs = ms };
}
