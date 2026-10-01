namespace SocAutoTask.Format;

/// <summary>CRC-32 IEEE (el de zip y png). Propio para no añadir dependencias al reproductor.</summary>
public sealed class Crc32
{
    private static readonly uint[] Table = BuildTable();
    private uint _value = 0xFFFFFFFF;

    public uint Value => ~_value;

    public void Append(ReadOnlySpan<byte> data)
    {
        var crc = _value;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        _value = crc;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var c = new Crc32();
        c.Append(data);
        return c.Value;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}

/// <summary>Flujo que pasa los bytes a otro y va calculando su CRC (al escribir o al leer).</summary>
internal sealed class CrcStream(Stream inner, bool leaveOpen, Crc32? crc = null) : Stream
{
    /// <summary>El CRC que se va acumulando (se puede pasar uno ya empezado, con la cabecera).</summary>
    public Crc32 Crc { get; } = crc ?? new();

    public long Count { get; private set; }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = inner.Read(buffer);
        Crc.Append(buffer[..n]);
        Count += n;
        return n;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Crc.Append(buffer);
        Count += buffer.Length;
        inner.Write(buffer);
    }

    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
