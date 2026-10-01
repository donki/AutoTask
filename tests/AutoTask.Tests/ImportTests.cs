using System.Buffers.Binary;
using SocAutoTask.Compile;
using SocAutoTask.Format;
using SocAutoTask.Model;

namespace SocAutoTask.Tests;

/// <summary>Importar .rec (RF-28) y abrir cualquier cosa (RF-26).</summary>
public sealed class ImportTests
{
    private static byte[] Rec(params (uint Message, uint L, uint H, uint Time)[] records)
    {
        var bytes = new byte[records.Length * RecImporter.RecordSize];
        for (var i = 0; i < records.Length; i++)
        {
            var span = bytes.AsSpan(i * 20);
            BinaryPrimitives.WriteUInt32LittleEndian(span, records[i].Message);
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], records[i].L);
            BinaryPrimitives.WriteUInt32LittleEndian(span[8..], records[i].H);
            BinaryPrimitives.WriteUInt32LittleEndian(span[12..], records[i].Time);
            BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0x00010ABC);
        }
        return bytes;
    }

    [Fact]
    public void Rec_RatonYTeclado_ConSusEsperas()
    {
        var data = Rec(
            (0x0200, 100, 200, 5000),      // movimiento
            (0x0201, 100, 200, 5016),      // izq. pulsado
            (0x0202, 100, 200, 5100),      // izq. soltado
            (0x0204, 7, 8, 5100),          // der.
            (0x0205, 7, 8, 5101),
            (0x0207, 1, 2, 5102),          // central
            (0x0208, 1, 2, 5103),
            (0x020B, 3, 4, 5104),          // X
            (0x020C, 3, 4, 5105),
            (0x020A, 5, 6, 5106),          // rueda
            (0x020E, 5, 6, 5107),          // rueda horizontal
            (0x0100, 0x1E41, 1, 5200),     // A pulsada (vk 0x41, scan 0x1E)
            (0x0101, 0x1E41, 1, 5250),
            (0x0104, 0x0012, 0x38, 5300),  // Alt (sys), scan en paramH
            (0x0105, 0x0012, 0x38, 5310),
            (0x0100, 0x0025, 0x804B, 5400) // flecha izquierda, extendida
        );
        var r = RecImporter.Parse(data);

        Assert.Equal(16, r.Count);
        Assert.Equal(MacroEvent.Move(100, 200), r.Events[0]);
        Assert.Equal(MacroEvent.Down(MouseButton.Left, 100, 200, 16), r.Events[1]);
        Assert.Equal(MacroEvent.Up(MouseButton.Left, 100, 200, 84), r.Events[2]);
        Assert.Equal(MouseButton.Right, r.Events[3].Button);
        Assert.Equal(MouseButton.Middle, r.Events[5].Button);
        Assert.Equal(MouseButton.X1, r.Events[7].Button);
        Assert.Equal(EventKind.Wheel, r.Events[9].Kind);
        Assert.Equal(120, r.Events[9].Data);
        Assert.Equal(EventKind.HWheel, r.Events[10].Kind);
        Assert.Equal(EventKind.KeyDown, r.Events[11].Kind);
        Assert.Equal(0x41, r.Events[11].VirtualKey);
        Assert.Equal(0x1E, r.Events[11].ScanCode);
        Assert.Equal(93, r.Events[11].DelayMs);
        Assert.Equal(EventKind.KeyUp, r.Events[12].Kind);
        Assert.Equal(0x12, r.Events[13].VirtualKey);
        Assert.Equal(0x38, r.Events[13].ScanCode);
        Assert.Equal(EventKind.KeyUp, r.Events[14].Kind);
        Assert.True(r.Events[15].IsExtended);
        Assert.Equal(400, r.TotalMs);
    }

    [Fact]
    public void Rec_MensajesDesconocidos_PasanSuEsperaAlSiguiente()
    {
        var records = Enumerable.Range(0, 20).Select(i => ((uint)0x0200, (uint)i, (uint)i, (uint)(1000 + i * 10))).ToList();
        records.Insert(5, (0x0999, 0, 0, 1045));
        var r = RecImporter.Parse(Rec([.. records]));
        Assert.Equal(20, r.Count);
        Assert.Equal(190, r.TotalMs);
    }

    [Fact]
    public void Rec_GetTickCountDaLaVuelta_SinEsperasLocas()
    {
        var r = RecImporter.Parse(Rec((0x0200, 1, 1, uint.MaxValue - 5), (0x0200, 2, 2, 10)));
        Assert.Equal(16, r.Events[1].DelayMs);
    }

    [Fact]
    public void Rec_RetrocesoPequeño_SinEspera_YGrande_SeRechaza()
    {
        var small = RecImporter.Parse(Rec((0x0200, 1, 1, 5000), (0x0200, 2, 2, 4990)));
        Assert.Equal(0, small.Events[1].DelayMs);
        var ex = Assert.Throws<RecordingFormatException>(() => RecImporter.Parse(Rec((0x0200, 1, 1, 50000), (0x0200, 2, 2, 40000))));
        Assert.Equal(FormatProblem.NotARec, ex.Problem);
    }

    [Fact]
    public void Rec_TamañoQueNoCuadra_SeRechaza()
    {
        var ex = Assert.Throws<RecordingFormatException>(() => RecImporter.Parse(new byte[21]));
        Assert.Equal(FormatProblem.NotARec, ex.Problem);
        Assert.Equal("FormatNotARec", ex.LocKey);
    }

    [Fact]
    public void Rec_Vacio_SeRechaza() =>
        Assert.Equal(FormatProblem.Empty, Assert.Throws<RecordingFormatException>(() => RecImporter.Parse([])).Problem);

    [Fact]
    public void Rec_ConBasura_SeRechaza()
    {
        // Un fichero cualquiera de tamaño multiplo de 20: casi nada son mensajes de raton o teclado.
        var random = new Random(42);
        var junk = new byte[2000];
        random.NextBytes(junk);
        Assert.Equal(FormatProblem.NotARec, Assert.Throws<RecordingFormatException>(() => RecImporter.Parse(junk)).Problem);
    }

    [Fact]
    public void Rec_QueEsUnSoctask_SeRechaza()
    {
        var bytes = Samples.Bytes(Samples.Recording());
        var padded = bytes.Concat(new byte[20 - bytes.Length % 20]).ToArray();
        Assert.Equal(FormatProblem.NotARec, Assert.Throws<RecordingFormatException>(() => RecImporter.Parse(padded)).Problem);
    }

    [Fact]
    public void Rec_TeclaSinCodigo_SeIgnora()
    {
        var r = RecImporter.Parse(Rec(Enumerable.Range(0, 10).Select(i => ((uint)0x0100, (uint)0x41, 0u, (uint)i)).Append(((uint)0x0100, 0u, 0u, 20u)).ToArray()));
        Assert.Equal(10, r.Count);
    }

    [Fact]
    public void Loader_Abre_Soctask_Rec_YExe()
    {
        using var dir = new TempFolder();
        var soctask = dir.File("a.soctask");
        RecordingFile.Save(Samples.Recording(), soctask);
        var (r1, k1) = RecordingLoader.Open(soctask);
        Assert.Equal(LoadedKind.SocTask, k1);
        Assert.Equal(Samples.AllKinds().Count, r1.Count);

        var rec = dir.File("b.REC");
        File.WriteAllBytes(rec, Rec((0x0200, 1, 1, 0), (0x0201, 1, 1, 10), (0x0202, 1, 1, 20)));
        var (r2, k2) = RecordingLoader.Open(rec);
        Assert.Equal(LoadedKind.Rec, k2);
        Assert.Equal(3, r2.Count);

        var exe = dir.File("c.exe");
        ExeBuilder.Build(new MemoryStream(ExeTests.FakePlayer()), Samples.Recording(), new ExeOptions(), exe);
        var (r3, k3) = RecordingLoader.Open(exe);
        Assert.Equal(LoadedKind.CompiledExe, k3);
        Assert.Equal(Samples.AllKinds(), r3.Events);
    }

    [Fact]
    public void Loader_GrabacionSinEventos_DiceVacia()
    {
        using var dir = new TempFolder();
        var path = dir.File("vacia.soctask");
        RecordingFile.Save(new Recording([]), path);
        Assert.Equal(FormatProblem.Empty, Assert.Throws<RecordingFormatException>(() => RecordingLoader.Open(path)).Problem);
    }

    [Fact]
    public void Loader_FicheroQueNoExiste_LanzaDeFichero()
    {
        Assert.Throws<FileNotFoundException>(() => RecordingLoader.Open(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".soctask")));
    }

    [Theory]
    [InlineData("x.soctask", true)]
    [InlineData("X.SOCTASK", true)]
    [InlineData("x.rec", true)]
    [InlineData("x.exe", true)]
    [InlineData("x.txt", false)]
    [InlineData("x", false)]
    public void Loader_SabeQueAbre(string name, bool expected) => Assert.Equal(expected, RecordingLoader.IsSupported(name));
}
