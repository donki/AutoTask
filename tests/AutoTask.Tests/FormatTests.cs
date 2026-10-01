using System.Buffers.Binary;
using System.Diagnostics;
using SocAutoTask.Format;
using SocAutoTask.Model;

namespace SocAutoTask.Tests;

/// <summary>Formato .soctask (RF-22..24, CA-06).</summary>
public sealed class FormatTests
{
    [Fact]
    public void GuardarYAbrir_DevuelveLoMismo_ConTodosLosTiposDeEvento()
    {
        var original = Samples.Recording();
        using var ms = new MemoryStream(Samples.Bytes(original));
        var read = RecordingFile.Read(ms);

        Assert.Equal(original.Events, read.Events);
        Assert.Equal(original.Options, read.Options);
        Assert.Equal(original.Screen, read.Screen);
        Assert.Equal(original.RecordedUtc, read.RecordedUtc);
        Assert.Equal(original.TotalMs, read.TotalMs);
    }

    [Fact]
    public void Grabacion_Vacia_SeGuardaYSeLee()
    {
        var empty = new Recording([], new PlaybackOptions());
        using var ms = new MemoryStream(Samples.Bytes(empty));
        var read = RecordingFile.Read(ms);
        Assert.True(read.IsEmpty);
        Assert.Equal(0, read.TotalMs);
    }

    [Theory]
    [InlineData(RepeatMode.Once, 1, 0, 0.5)]
    [InlineData(RepeatMode.Times, 100000, 3600000, 1000)]
    [InlineData(RepeatMode.Continuous, 5, 10, 0.1)]
    public void Opciones_SeConservan(RepeatMode repeat, int times, int pause, double speed)
    {
        var options = new PlaybackOptions { Repeat = repeat, Times = times, PauseBetweenMs = pause, Speed = speed };
        using var ms = new MemoryStream(Samples.Bytes(Samples.Recording(options)));
        Assert.Equal(options, RecordingFile.Read(ms).Options);
    }

    [Fact]
    public void Empieza_PorLaFirma_YLaVersion()
    {
        var bytes = Samples.Bytes(Samples.Recording());
        Assert.True(RecordingFile.HasMagic(bytes));
        Assert.Equal("SOCTASK\x1A"u8.ToArray(), bytes[..8]);
        Assert.Equal(RecordingFile.CurrentVersion, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(RecordingFile.OptionsLength, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)));
    }

    [Fact]
    public void Cualquier_ByteCambiado_SeDetecta()
    {
        // CA-06: no hay byte que se pueda cambiar sin que se note (la cabecera tambien entra en el CRC).
        var bytes = Samples.Bytes(Samples.Recording());
        for (var i = 0; i < bytes.Length; i++)
        {
            var copy = (byte[])bytes.Clone();
            copy[i] ^= 0x5A;
            var ex = Record.Exception(() => RecordingFile.Read(new MemoryStream(copy))) as RecordingFormatException;
            Assert.True(ex is not null, $"byte {i} de {bytes.Length} cambiado y no se ha notado");
            Assert.True(ex!.Problem is FormatProblem.Corrupt or FormatProblem.Truncated or FormatProblem.NotARecording or FormatProblem.NewerVersion,
                $"byte {i}: {ex.Problem}");
        }
    }

    [Fact]
    public void Cortado_EnCualquierPunto_DiceIncompletoODañado()
    {
        var bytes = Samples.Bytes(Samples.Recording());
        for (var length = 0; length < bytes.Length; length++)
        {
            var ex = Assert.Throws<RecordingFormatException>(() => RecordingFile.Read(new MemoryStream(bytes[..length])));
            var expected = length < 8 ? new[] { FormatProblem.NotARecording, FormatProblem.Truncated } : [FormatProblem.Truncated, FormatProblem.Corrupt];
            Assert.Contains(ex.Problem, expected);
        }
    }

    [Fact]
    public void Datos_DeMasAlFinal_NoMolestan()
    {
        // El exe compilado lleva cosas detras de la grabacion: el lector se para donde acaba.
        var bytes = Samples.Bytes(Samples.Recording()).Concat(new byte[] { 1, 2, 3, 4 }).ToArray();
        using var ms = new MemoryStream(bytes);
        Assert.Equal(Samples.AllKinds().Count, RecordingFile.Read(ms).Count);
        Assert.Equal(bytes.Length - 4, ms.Position);
    }

    [Fact]
    public void OtraFirma_NoEsUnaGrabacion()
    {
        var ex = Assert.Throws<RecordingFormatException>(() => RecordingFile.Read(new MemoryStream("PK\x03\x04 esto es un zip cualquiera"u8.ToArray())));
        Assert.Equal(FormatProblem.NotARecording, ex.Problem);
        Assert.Equal("FormatNotARecording", ex.LocKey);
    }

    [Fact]
    public void VersionFutura_PideActualizar()
    {
        var bytes = Samples.Bytes(Samples.Recording());
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 2);
        Assert.Equal(FormatProblem.NewerVersion, Assert.Throws<RecordingFormatException>(() => RecordingFile.Read(new MemoryStream(bytes))).Problem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(2_000_000)]
    public void CabeceraDeOpciones_ImposibleONula_EsDaño(int length)
    {
        var bytes = Samples.Bytes(Samples.Recording());
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), length);
        Assert.Equal(FormatProblem.Corrupt, Assert.Throws<RecordingFormatException>(() => RecordingFile.Read(new MemoryStream(bytes))).Problem);
    }

    [Fact]
    public void Version0_EsDaño()
    {
        var bytes = Samples.Bytes(Samples.Recording());
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 0);
        Assert.Equal(FormatProblem.Corrupt, Assert.Throws<RecordingFormatException>(() => RecordingFile.Read(new MemoryStream(bytes))).Problem);
    }

    [Fact]
    public void CabeceraMasLarga_DeUnaVersionFutura1_SeLeeIgual()
    {
        // Los lectores ignoran lo que no conocen dentro de las opciones (se escribe a mano un H mayor).
        var original = Samples.Recording();
        var bytes = Samples.Bytes(original);
        var head = bytes[..(16 + RecordingFile.OptionsLength)];
        var rest = bytes[(16 + RecordingFile.OptionsLength)..];
        var longer = head.Concat(new byte[16]).Concat(rest).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(longer.AsSpan(12), RecordingFile.OptionsLength + 16);
        // El CRC cubre la cabecera: se recalcula como lo haria esa version.
        var crc = new Crc32();
        crc.Append(longer.AsSpan(0, 16 + RecordingFile.OptionsLength + 16));
        var length = BinaryPrimitives.ReadInt32LittleEndian(rest);
        crc.Append(rest.AsSpan(4, length));
        crc.Append(rest.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(longer.AsSpan(longer.Length - 4), crc.Value);
        Assert.Equal(original.Events, RecordingFile.Read(new MemoryStream(longer)).Events);
    }

    [Fact]
    public void FlujoSinPosicion_SeEscribeIgual()
    {
        var original = Samples.Recording();
        using var inner = new MemoryStream();
        using (var forward = new ForwardOnlyStream(inner))
            RecordingFile.Write(original, forward);
        Assert.Equal(Samples.Bytes(original), inner.ToArray());
    }

    [Fact]
    public void GrabacionLarga_UnMillonDeEventos_EsPequeñaYRapida()
    {
        // RNF-05: una hora de movimientos cabe holgada; el fichero comprime mucho.
        var events = new List<MacroEvent>(1_000_000);
        for (var i = 0; i < 1_000_000; i++)
            events.Add(MacroEvent.Move(i % 1920, i / 1920 % 1080, 8));
        var recording = new Recording(events);
        var watch = Stopwatch.StartNew();
        var bytes = Samples.Bytes(recording);
        var read = RecordingFile.Read(new MemoryStream(bytes));
        watch.Stop();
        Assert.Equal(1_000_000, read.Count);
        Assert.Equal(events[^1], read.Events[^1]);
        Assert.True(bytes.Length < 2 * 1024 * 1024, $"{bytes.Length} bytes");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), watch.Elapsed.ToString());
    }

    [Fact]
    public void Guardar_EnDisco_YAbrir()
    {
        using var dir = new TempFolder();
        var path = dir.File("prueba.soctask");
        RecordingFile.Save(Samples.Recording(), path);
        Assert.Equal(Samples.AllKinds(), RecordingFile.Load(path).Events);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Guardar_SiFalla_DejaElAnteriorIntacto()
    {
        // RF-24: el definitivo es de solo lectura: el cambio final falla y el anterior sigue como estaba.
        using var dir = new TempFolder();
        var path = dir.File("anterior.soctask");
        var before = new Recording([MacroEvent.Move(1, 1)]);
        RecordingFile.Save(before, path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.ThrowsAny<Exception>(() => RecordingFile.Save(Samples.Recording(), path));
        File.SetAttributes(path, FileAttributes.Normal);
        Assert.Equal(before.Events, RecordingFile.Load(path).Events);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Evento_ConTipoBotonOTeclaImposibles_EsDaño()
    {
        Assert.Equal(FormatProblem.Corrupt, Assert.Throws<RecordingFormatException>(() => RecordingFile.DecodeEvent(new MemoryStream([9, 0]))).Problem);
        Assert.Equal(FormatProblem.Corrupt, Assert.Throws<RecordingFormatException>(() => RecordingFile.DecodeEvent(new MemoryStream([1, 0, 0, 0, 7]))).Problem);
        Assert.Equal(FormatProblem.Corrupt, Assert.Throws<RecordingFormatException>(() => RecordingFile.DecodeEvent(new MemoryStream([5, 0, 0, 0]))).Problem);
        Assert.Throws<EndOfStreamException>(() => RecordingFile.DecodeEvent(new MemoryStream([5, 0, 0x41])));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(127u)]
    [InlineData(128u)]
    [InlineData(16383u)]
    [InlineData(16384u)]
    [InlineData(uint.MaxValue)]
    public void VarInt_IdaYVuelta(uint value)
    {
        var buffer = new byte[5];
        var n = VarInt.Write(buffer, value);
        Assert.Equal(value, VarInt.Read(new MemoryStream(buffer, 0, n)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void VarIntConSigno_IdaYVuelta(int value)
    {
        var buffer = new byte[5];
        var n = VarInt.WriteSigned(buffer, value);
        Assert.Equal(value, VarInt.ReadSigned(new MemoryStream(buffer, 0, n)));
    }

    [Fact]
    public void VarInt_DemasiadoLargo_EsDaño()
    {
        Assert.Throws<RecordingFormatException>(() => VarInt.Read(new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF, 0x7F])));
        Assert.Throws<RecordingFormatException>(() => VarInt.Read(new MemoryStream([0x80, 0x80, 0x80, 0x80, 0x80, 0x01])));
    }

    [Fact]
    public void Crc32_VectorConocido()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
        Assert.Equal(0u, Crc32.Compute([]));
    }

    /// <summary>Un flujo que no se puede mover (como una tuberia).</summary>
    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
