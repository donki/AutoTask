using System.Buffers.Binary;
using System.IO.Compression;
using SocAutoTask.Model;

namespace SocAutoTask.Format;

/// <summary>
/// Lee y escribe el formato <c>.soctask</c> (ARQUITECTURA §4): cabecera con firma y version,
/// opciones, eventos comprimidos con Deflate y CRC-32 de la cabecera y del bloque comprimido.
/// Todo por flujo.
/// </summary>
public static class RecordingFile
{
    public const string Extension = ".soctask";
    public const ushort CurrentVersion = 1;
    public const int OptionsLength = 64;

    /// <summary>"SOCTASK" + 0x1A (como el de PNG: corta la salida si alguien lo vuelca a una consola).</summary>
    public static ReadOnlySpan<byte> Magic => "SOCTASK\x1A"u8;

    /// <summary>Tope de eventos de un fichero: muy por encima de un dia de grabacion; evita reservar memoria absurda.</summary>
    public const long MaxEvents = 50_000_000;

    // Lo minimo de opciones que entiende la version 1 (hasta la duracion total).
    private const int KnownOptionsLength = 57;

    // =====================================================================
    //  Escribir
    // =====================================================================

    /// <summary>
    /// Guarda en un fichero sin dejarlo a medias (RF-24): se escribe a <c>.tmp</c> y se cambia por
    /// el definitivo al final. Si falla, el anterior sigue intacto.
    /// </summary>
    public static void Save(Recording recording, string path)
    {
        var full = Path.GetFullPath(path);
        var temp = full + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024))
                Write(recording, stream);
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Escribe la grabacion entera en el flujo (si no se puede mover, se monta antes en memoria).</summary>
    public static void Write(Recording recording, Stream output)
    {
        if (!output.CanSeek)
        {
            using var buffer = new MemoryStream();
            Write(recording, buffer);
            buffer.Position = 0;
            buffer.CopyTo(output);
            return;
        }

        Span<byte> head = stackalloc byte[16 + OptionsLength];
        head.Clear();
        Magic.CopyTo(head);
        BinaryPrimitives.WriteUInt16LittleEndian(head[8..], CurrentVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(head[10..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(head[12..], OptionsLength);
        WriteOptions(recording, head[16..]);
        output.Write(head);
        // El CRC cubre la cabecera, los bytes comprimidos y su longitud: cualquier byte cambiado se
        // nota (tambien los bits de relleno de Deflate, que no cambiarian los eventos).
        var crc = new Crc32();
        crc.Append(head);

        var lengthAt = output.Position;
        Span<byte> four = stackalloc byte[4];
        output.Write(four);   // longitud del bloque comprimido: se rellena al final

        var start = output.Position;
        using (var crcStream = new CrcStream(output, leaveOpen: true, crc))
        using (var deflate = new DeflateStream(crcStream, CompressionLevel.Optimal, leaveOpen: true))
        using (var buffered = new BufferedStream(deflate, 64 * 1024))
        {
            Span<byte> scratch = stackalloc byte[32];
            foreach (var e in recording.Events)
                buffered.Write(scratch[..EncodeEvent(e, scratch)]);
            buffered.Flush();
        }
        var end = output.Position;
        var compressed = end - start;
        if (compressed > int.MaxValue)
            throw new IOException("Grabacion demasiado grande para el formato 1.");
        BinaryPrimitives.WriteInt32LittleEndian(four, (int)compressed);
        crc.Append(four);

        BinaryPrimitives.WriteUInt32LittleEndian(four, crc.Value);
        output.Write(four);
        var after = output.Position;
        output.Position = lengthAt;
        BinaryPrimitives.WriteInt32LittleEndian(four, (int)compressed);
        output.Write(four);
        output.Position = after;
        output.Flush();
    }

    private static void WriteOptions(Recording r, Span<byte> o)
    {
        var opt = r.Options.Normalized();
        BinaryPrimitives.WriteDoubleLittleEndian(o, opt.Speed);
        o[8] = (byte)opt.Repeat;
        BinaryPrimitives.WriteInt32LittleEndian(o[9..], opt.Times);
        BinaryPrimitives.WriteInt32LittleEndian(o[13..], opt.PauseBetweenMs);
        BinaryPrimitives.WriteInt32LittleEndian(o[17..], r.Screen.Left);
        BinaryPrimitives.WriteInt32LittleEndian(o[21..], r.Screen.Top);
        BinaryPrimitives.WriteInt32LittleEndian(o[25..], r.Screen.Width);
        BinaryPrimitives.WriteInt32LittleEndian(o[29..], r.Screen.Height);
        BinaryPrimitives.WriteInt64LittleEndian(o[33..], r.RecordedUtc.ToUniversalTime().Ticks);
        BinaryPrimitives.WriteInt64LittleEndian(o[41..], r.Count);
        BinaryPrimitives.WriteInt64LittleEndian(o[49..], r.TotalMs);
    }

    /// <summary>Un evento en bytes (ARQUITECTURA §4). Devuelve cuantos ha escrito.</summary>
    internal static int EncodeEvent(in MacroEvent e, Span<byte> buffer)
    {
        var n = 0;
        buffer[n++] = (byte)e.Kind;
        n += VarInt.Write(buffer[n..], (uint)Math.Max(0, e.DelayMs));
        switch (e.Kind)
        {
            case EventKind.MouseMove:
                n += VarInt.WriteSigned(buffer[n..], e.X);
                n += VarInt.WriteSigned(buffer[n..], e.Y);
                break;
            case EventKind.MouseDown or EventKind.MouseUp:
                n += VarInt.WriteSigned(buffer[n..], e.X);
                n += VarInt.WriteSigned(buffer[n..], e.Y);
                buffer[n++] = (byte)e.Button;
                break;
            case EventKind.Wheel or EventKind.HWheel:
                n += VarInt.WriteSigned(buffer[n..], e.X);
                n += VarInt.WriteSigned(buffer[n..], e.Y);
                n += VarInt.WriteSigned(buffer[n..], e.Data);
                break;
            case EventKind.KeyDown or EventKind.KeyUp:
                n += VarInt.Write(buffer[n..], (uint)e.Data);
                buffer[n++] = e.Flags;
                break;
        }
        return n;
    }

    // =====================================================================
    //  Leer
    // =====================================================================

    /// <summary>Lee un fichero .soctask. Lanza <see cref="RecordingFormatException"/> si no vale (RF-23).</summary>
    public static Recording Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        return Read(stream);
    }

    /// <summary>¿Empieza por la firma de .soctask?</summary>
    public static bool HasMagic(ReadOnlySpan<byte> start) => start.Length >= Magic.Length && start[..Magic.Length].SequenceEqual(Magic);

    public static Recording Read(Stream input)
    {
        Span<byte> fixedHead = stackalloc byte[16];
        if (ReadFully(input, fixedHead) < 16)
        {
            // Unos pocos bytes: si ni siquiera llevan la firma, no es nuestro.
            throw HasMagic(fixedHead) ? Bad(FormatProblem.Truncated, "cabecera corta") : Bad(FormatProblem.NotARecording, "fichero demasiado corto");
        }
        if (!HasMagic(fixedHead))
            throw Bad(FormatProblem.NotARecording, "firma distinta");

        var version = BinaryPrimitives.ReadUInt16LittleEndian(fixedHead[8..]);
        if (version > CurrentVersion)
            throw Bad(FormatProblem.NewerVersion, $"formato {version}");
        if (version == 0)
            throw Bad(FormatProblem.Corrupt, "version 0");

        var optionsLength = BinaryPrimitives.ReadInt32LittleEndian(fixedHead[12..]);
        if (optionsLength < KnownOptionsLength || optionsLength > 1024 * 1024)
            throw Bad(FormatProblem.Corrupt, $"opciones de {optionsLength} bytes");

        var options = new byte[optionsLength];
        if (ReadFully(input, options) < optionsLength)
            throw Bad(FormatProblem.Truncated, "opciones cortas");

        var o = options.AsSpan();
        var speed = BinaryPrimitives.ReadDoubleLittleEndian(o);
        if (!double.IsFinite(speed) || speed < PlaybackOptions.MinSpeed || speed > PlaybackOptions.MaxSpeed)
            throw Bad(FormatProblem.Corrupt, $"velocidad {speed}");
        if (o[8] > (byte)RepeatMode.Continuous)
            throw Bad(FormatProblem.Corrupt, $"repeticion {o[8]}");
        var playback = new PlaybackOptions
        {
            Speed = speed,
            Repeat = (RepeatMode)o[8],
            Times = BinaryPrimitives.ReadInt32LittleEndian(o[9..]),
            PauseBetweenMs = BinaryPrimitives.ReadInt32LittleEndian(o[13..]),
        }.Normalized();
        var screen = new ScreenRect(
            BinaryPrimitives.ReadInt32LittleEndian(o[17..]), BinaryPrimitives.ReadInt32LittleEndian(o[21..]),
            BinaryPrimitives.ReadInt32LittleEndian(o[25..]), BinaryPrimitives.ReadInt32LittleEndian(o[29..]));
        var ticks = BinaryPrimitives.ReadInt64LittleEndian(o[33..]);
        var recorded = ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.UnixEpoch;
        var count = BinaryPrimitives.ReadInt64LittleEndian(o[41..]);
        var totalMs = BinaryPrimitives.ReadInt64LittleEndian(o[49..]);
        if (count < 0 || count > MaxEvents)
            throw Bad(FormatProblem.Corrupt, $"{count} eventos");

        Span<byte> four = stackalloc byte[4];
        if (ReadFully(input, four) < 4)
            throw Bad(FormatProblem.Truncated, "sin longitud de bloque");
        var compressedLength = BinaryPrimitives.ReadInt32LittleEndian(four);
        if (compressedLength < 0)
            throw Bad(FormatProblem.Corrupt, "longitud de bloque negativa");

        var crc = new Crc32();
        crc.Append(fixedHead);
        crc.Append(options);
        var events = new List<MacroEvent>((int)Math.Min(count, 1_000_000));
        var limited = new LimitedStream(input, compressedLength);
        using var compressedCrc = new CrcStream(limited, leaveOpen: true, crc);
        try
        {
            using var deflate = new DeflateStream(compressedCrc, CompressionMode.Decompress, leaveOpen: true);
            using var buffered = new BufferedStream(deflate, 64 * 1024);
            for (long i = 0; i < count; i++)
                events.Add(DecodeEvent(buffered));
            if (buffered.ReadByte() != -1)
                throw Bad(FormatProblem.Corrupt, "sobran datos tras el ultimo evento");
        }
        catch (EndOfStreamException)
        {
            throw limited.HitEndEarly ? Bad(FormatProblem.Truncated, "bloque de eventos corto") : Bad(FormatProblem.Corrupt, "faltan eventos");
        }
        catch (InvalidDataException ex)
        {
            throw limited.HitEndEarly ? Bad(FormatProblem.Truncated, "bloque comprimido corto") : Bad(FormatProblem.Corrupt, "deflate: " + ex.Message);
        }

        // Lo que el descompresor no leyo del bloque tambien pasa por el CRC.
        Span<byte> skip = stackalloc byte[4096];
        while (compressedCrc.Read(skip) > 0)
        {
        }
        if (limited.HitEndEarly)
            throw Bad(FormatProblem.Truncated, "bloque comprimido corto");
        BinaryPrimitives.WriteInt32LittleEndian(four, compressedLength);
        crc.Append(four);
        if (ReadFully(input, four) < 4)
            throw Bad(FormatProblem.Truncated, "sin CRC");
        if (BinaryPrimitives.ReadUInt32LittleEndian(four) != crc.Value)
            throw Bad(FormatProblem.Corrupt, "CRC distinto");

        var recording = new Recording(events, playback, screen, recorded);
        if (recording.TotalMs != totalMs)
            throw Bad(FormatProblem.Corrupt, $"duracion {recording.TotalMs} en vez de {totalMs}");
        return recording;
    }

    internal static MacroEvent DecodeEvent(Stream s)
    {
        var kindByte = s.ReadByte();
        if (kindByte < 0)
            throw new EndOfStreamException();
        if (kindByte > (byte)EventKind.Wait)
            throw Bad(FormatProblem.Corrupt, $"tipo de evento {kindByte}");
        var kind = (EventKind)kindByte;
        var delay = VarInt.Read(s);
        if (delay > int.MaxValue)
            throw Bad(FormatProblem.Corrupt, "espera imposible");
        var e = new MacroEvent { Kind = kind, DelayMs = (int)delay };
        switch (kind)
        {
            case EventKind.MouseMove:
                e = e with { X = VarInt.ReadSigned(s), Y = VarInt.ReadSigned(s) };
                break;
            case EventKind.MouseDown or EventKind.MouseUp:
                e = e with { X = VarInt.ReadSigned(s), Y = VarInt.ReadSigned(s) };
                var button = ReadByteOrThrow(s);
                if (button > (byte)MouseButton.X2)
                    throw Bad(FormatProblem.Corrupt, $"boton {button}");
                e = e with { Button = (MouseButton)button };
                break;
            case EventKind.Wheel or EventKind.HWheel:
                e = e with { X = VarInt.ReadSigned(s), Y = VarInt.ReadSigned(s), Data = VarInt.ReadSigned(s) };
                break;
            case EventKind.KeyDown or EventKind.KeyUp:
                e = e with { Data = (int)VarInt.Read(s), Flags = ReadByteOrThrow(s) };
                if (e.VirtualKey is 0 or > 0xFE)
                    throw Bad(FormatProblem.Corrupt, $"tecla {e.VirtualKey}");
                break;
        }
        return e;
    }

    private static byte ReadByteOrThrow(Stream s)
    {
        var b = s.ReadByte();
        return b < 0 ? throw new EndOfStreamException() : (byte)b;
    }

    private static int ReadFully(Stream s, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = s.Read(buffer[total..]);
            if (n == 0)
                break;
            total += n;
        }
        return total;
    }

    private static RecordingFormatException Bad(FormatProblem problem, string detail) => new(problem, detail);

    /// <summary>Deja leer como mucho N bytes del flujo de debajo y sabe si este se acabo antes.</summary>
    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long _left = limit;

        public bool HitEndEarly { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_left <= 0)
                return 0;
            var n = inner.Read(buffer[..(int)Math.Min(buffer.Length, _left)]);
            if (n == 0)
                HitEndEarly = true;
            _left -= n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Enteros de longitud variable (7 bits por byte) y zigzag para los que llevan signo.</summary>
internal static class VarInt
{
    public static int Write(Span<byte> buffer, uint value)
    {
        var n = 0;
        while (value >= 0x80)
        {
            buffer[n++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buffer[n++] = (byte)value;
        return n;
    }

    public static int WriteSigned(Span<byte> buffer, int value) => Write(buffer, (uint)((value << 1) ^ (value >> 31)));

    public static uint Read(Stream s)
    {
        uint result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = s.ReadByte();
            if (b < 0)
                throw new EndOfStreamException();
            if (shift == 28 && (b & 0xF0) != 0)
                throw new RecordingFormatException(FormatProblem.Corrupt, "entero demasiado largo");
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
        }
        throw new RecordingFormatException(FormatProblem.Corrupt, "entero demasiado largo");
    }

    public static int ReadSigned(Stream s)
    {
        var v = Read(s);
        return (int)(v >> 1) ^ -(int)(v & 1);
    }
}
