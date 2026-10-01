using System.Buffers.Binary;
using SocAutoTask.Model;

namespace SocAutoTask.Format;

/// <summary>
/// Importa (experimental) las grabaciones <c>.rec</c> de la utilidad clasica de grabar macros:
/// una lista de estructuras <c>EVENTMSG</c> de 20 bytes (ARQUITECTURA §5). Valida la forma antes de
/// aceptar nada y explica el rechazo (RF-28).
/// </summary>
public static class RecImporter
{
    public const string Extension = ".rec";
    public const int RecordSize = 20;

    /// <summary>Un .rec mas grande que esto no es razonable (unos 5 millones de eventos).</summary>
    public const long MaxBytes = 100L * 1024 * 1024;

    private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmRButtonDown = 0x0204, WmRButtonUp = 0x0205;
    private const uint WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmMouseWheel = 0x020A, WmXButtonDown = 0x020B, WmXButtonUp = 0x020C, WmMouseHWheel = 0x020E;

    public static Recording Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes)
            throw new RecordingFormatException(FormatProblem.NotARec, $"{info.Length} bytes");
        return Parse(File.ReadAllBytes(path));
    }

    public static Recording Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            throw new RecordingFormatException(FormatProblem.Empty, ".rec vacio");
        if (data.Length % RecordSize != 0)
            throw new RecordingFormatException(FormatProblem.NotARec, $"{data.Length} bytes no es multiplo de {RecordSize}");
        if (RecordingFile.HasMagic(data))
            throw new RecordingFormatException(FormatProblem.NotARec, "es un .soctask con otra extension");

        var count = data.Length / RecordSize;
        var events = new List<MacroEvent>(count);
        var known = 0;
        uint? lastTime = null;
        long pendingDelay = 0;
        for (var i = 0; i < count; i++)
        {
            var r = data.Slice(i * RecordSize, RecordSize);
            var message = BinaryPrimitives.ReadUInt32LittleEndian(r);
            var paramL = BinaryPrimitives.ReadUInt32LittleEndian(r[4..]);
            var paramH = BinaryPrimitives.ReadUInt32LittleEndian(r[8..]);
            var time = BinaryPrimitives.ReadUInt32LittleEndian(r[12..]);

            long delay = 0;
            if (lastTime is { } previous)
            {
                // GetTickCount da la vuelta cada 49,7 dias: la resta sin signo lo absorbe. Un salto
                // «hacia atras» de mas de 1 s es que no son tiempos.
                var diff = unchecked(time - previous);
                if (diff > 0x7FFFFFFF)
                {
                    if (unchecked(previous - time) > 1000)
                        throw new RecordingFormatException(FormatProblem.NotARec, $"el tiempo retrocede en el registro {i}");
                    diff = 0;
                }
                delay = diff;
            }
            lastTime = time;
            pendingDelay += delay;

            var e = Convert(message, paramL, paramH);
            if (e is null)
                continue;
            known++;
            events.Add(e.Value with { DelayMs = (int)Math.Min(int.MaxValue, pendingDelay) });
            pendingDelay = 0;
        }

        if (known == 0 || known < count * 0.9)
            throw new RecordingFormatException(FormatProblem.NotARec, $"solo {known} de {count} registros son de raton o teclado");
        if (events.Count > 0)
            events[0] = events[0] with { DelayMs = 0 };
        return new Recording(events);
    }

    private static MacroEvent? Convert(uint message, uint paramL, uint paramH)
    {
        int x = unchecked((int)paramL), y = unchecked((int)paramH);
        switch (message)
        {
            case WmMouseMove: return MacroEvent.Move(x, y);
            case WmLButtonDown: return MacroEvent.Down(MouseButton.Left, x, y);
            case WmLButtonUp: return MacroEvent.Up(MouseButton.Left, x, y);
            case WmRButtonDown: return MacroEvent.Down(MouseButton.Right, x, y);
            case WmRButtonUp: return MacroEvent.Up(MouseButton.Right, x, y);
            case WmMButtonDown: return MacroEvent.Down(MouseButton.Middle, x, y);
            case WmMButtonUp: return MacroEvent.Up(MouseButton.Middle, x, y);
            case WmXButtonDown or WmXButtonUp:
                // En los mensajes de raton del diario las coordenadas van en paramL/paramH y el boton X
                // no viene: se toma X1, que es «atras», el uso habitual.
                return message == WmXButtonDown ? MacroEvent.Down(MouseButton.X1, x, y) : MacroEvent.Up(MouseButton.X1, x, y);
            case WmMouseWheel or WmMouseHWheel:
                return MacroEvent.WheelAt(x, y, 120, horizontal: message == WmMouseHWheel);
            case WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp:
                var vk = (ushort)(paramL & 0xFF);
                if (vk == 0)
                    return null;
                var scan = (ushort)((paramL >> 8) & 0xFF);
                if (scan == 0)
                    scan = (ushort)(paramH & 0xFF);
                var extended = (paramH & 0x8000) != 0;
                return MacroEvent.Key(message is WmKeyDown or WmSysKeyDown, vk, scan, extended);
            default:
                return null;
        }
    }
}
