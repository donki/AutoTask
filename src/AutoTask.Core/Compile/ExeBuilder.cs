using System.Buffers.Binary;
using SocAutoTask.Format;
using SocAutoTask.Input;
using SocAutoTask.Model;

namespace SocAutoTask.Compile;

/// <summary>Opciones del exe compilado que no van en la grabacion (ARQUITECTURA §6).</summary>
public sealed record ExeOptions
{
    public const int Length = 32;

    public EmergencyKeys Emergency { get; init; } = EmergencyKeys.All;
    public int EscapeHoldMs { get; init; } = EmergencyStopLogic.DefaultEscapeHoldMs;
    public int CountdownSeconds { get; init; }

    internal void Write(Span<byte> o)
    {
        o.Clear();
        o[0] = 1;   // version de este bloque
        o[1] = (byte)(Emergency & EmergencyKeys.All);
        BinaryPrimitives.WriteInt32LittleEndian(o[4..], EscapeHoldMs);
        BinaryPrimitives.WriteInt32LittleEndian(o[8..], Math.Clamp(CountdownSeconds, 0, 60));
    }

    internal static ExeOptions Read(ReadOnlySpan<byte> o)
    {
        if (o[0] != 1)
            throw new RecordingFormatException(FormatProblem.NewerVersion, $"opciones del exe {o[0]}");
        return new ExeOptions
        {
            Emergency = (EmergencyKeys)o[1] & EmergencyKeys.All,
            EscapeHoldMs = BinaryPrimitives.ReadInt32LittleEndian(o[4..]),
            CountdownSeconds = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(o[8..]), 0, 60),
        };
    }
}

/// <summary>
/// Compila una grabacion a exe (RF-29..31): el reproductor tal cual, detras la grabacion en
/// formato .soctask y las opciones, y al final la longitud y una firma para encontrarlas.
/// </summary>
public static class ExeBuilder
{
    /// <summary>Firma del final del exe: "SOCTPAY1".</summary>
    public static ReadOnlySpan<byte> PayloadMagic => "SOCTPAY1"u8;

    public const int TrailerLength = 16;

    /// <summary>Escribe el exe en <paramref name="output"/> (a un .tmp y luego cambiado, como al guardar).</summary>
    public static void Build(Stream player, Recording recording, ExeOptions options, string output)
    {
        if (recording.IsEmpty)
            throw new RecordingFormatException(FormatProblem.Empty, "grabacion vacia");
        var full = Path.GetFullPath(output);
        var temp = full + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024))
            {
                player.CopyTo(file);
                if (file.Length < 1024 || !LooksLikePe(file))
                    throw new InvalidDataException("El reproductor incrustado no es un exe.");
                file.Position = file.Length;
                var start = file.Position;
                RecordingFile.Write(recording, file);
                Span<byte> tail = stackalloc byte[ExeOptions.Length + TrailerLength];
                options.Write(tail[..ExeOptions.Length]);
                var payloadLength = file.Position - start + ExeOptions.Length;
                BinaryPrimitives.WriteInt64LittleEndian(tail[ExeOptions.Length..], payloadLength);
                PayloadMagic.CopyTo(tail[(ExeOptions.Length + 8)..]);
                file.Write(tail);
            }
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static bool LooksLikePe(Stream s)
    {
        s.Position = 0;
        return s.ReadByte() == 'M' && s.ReadByte() == 'Z';
    }
}

/// <summary>Lo que el reproductor busca al final de su propio exe.</summary>
public static class PayloadReader
{
    /// <summary>¿Lleva grabacion este exe? (sin leerla entera)</summary>
    public static bool HasPayload(string exePath)
    {
        try
        {
            using var file = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return FindPayload(file) is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Lee la grabacion y las opciones de un exe compilado. Lanza RecordingFormatException si no hay o esta mal.</summary>
    public static (Recording Recording, ExeOptions Options) Read(string exePath)
    {
        using var file = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        return Read(file);
    }

    public static (Recording Recording, ExeOptions Options) Read(Stream file)
    {
        var length = FindPayload(file) ?? throw new RecordingFormatException(FormatProblem.NotARecording, "el exe no lleva grabacion");
        var start = file.Length - TrailerAndOptions - (length - ExeOptions.Length);
        file.Position = start;
        var recording = RecordingFile.Read(file);
        // Tras la grabacion vienen justo las opciones: si no, el .soctask y la longitud no cuadran.
        if (file.Position != file.Length - TrailerAndOptions)
            throw new RecordingFormatException(FormatProblem.Corrupt, "la grabacion no ocupa lo que dice la cola");
        Span<byte> o = stackalloc byte[ExeOptions.Length];
        file.ReadExactly(o);
        return (recording, ExeOptions.Read(o));
    }

    private const int TrailerAndOptions = ExeOptions.Length + ExeBuilder.TrailerLength;

    /// <summary>Longitud de la carga (grabacion + opciones) si el exe acaba con la firma.</summary>
    private static long? FindPayload(Stream file)
    {
        if (file.Length < TrailerAndOptions + 16)
            return null;
        Span<byte> trailer = stackalloc byte[ExeBuilder.TrailerLength];
        file.Position = file.Length - ExeBuilder.TrailerLength;
        file.ReadExactly(trailer);
        if (!trailer[8..].SequenceEqual(ExeBuilder.PayloadMagic))
            return null;
        var length = BinaryPrimitives.ReadInt64LittleEndian(trailer);
        if (length <= ExeOptions.Length || length > file.Length - ExeBuilder.TrailerLength)
            throw new RecordingFormatException(FormatProblem.Corrupt, $"longitud de carga {length}");
        return length;
    }
}
