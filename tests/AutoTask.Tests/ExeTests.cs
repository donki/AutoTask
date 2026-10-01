using System.Buffers.Binary;
using System.Diagnostics;
using SocAutoTask.Compile;
using SocAutoTask.Format;
using SocAutoTask.Input;
using SocAutoTask.Model;

namespace SocAutoTask.Tests;

/// <summary>Compilar a exe (RF-29..31, CA-07).</summary>
public sealed class ExeTests
{
    /// <summary>Un «reproductor» falso: empieza por MZ como un exe y tiene algo de tamaño.</summary>
    internal static byte[] FakePlayer()
    {
        var bytes = new byte[4096];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    /// <summary>El reproductor de verdad (Native AOT), si se ha compilado con tools\compilar-reproductor.ps1.</summary>
    internal static string? RealPlayer()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "artifacts", "player", "sOCAutoTaskPlayer.exe");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    [Fact]
    public void Compilar_YLeer_LaGrabacionYLasOpciones()
    {
        using var dir = new TempFolder();
        var exe = dir.File("macro.exe");
        var options = new ExeOptions { Emergency = EmergencyKeys.Pause | EmergencyKeys.EscapeHold, EscapeHoldMs = 1500, CountdownSeconds = 3 };
        ExeBuilder.Build(new MemoryStream(FakePlayer()), Samples.Recording(), options, exe);

        Assert.True(PayloadReader.HasPayload(exe));
        var (recording, read) = PayloadReader.Read(exe);
        Assert.Equal(Samples.AllKinds(), recording.Events);
        Assert.Equal(Samples.Recording().Options, recording.Options);
        Assert.Equal(options, read);
        Assert.Equal(FakePlayer(), File.ReadAllBytes(exe)[..4096]);   // el reproductor va tal cual delante
    }

    [Fact]
    public void Exe_SinGrabacion_NoEsUnaGrabacion()
    {
        using var dir = new TempFolder();
        var exe = dir.File("solo.exe");
        File.WriteAllBytes(exe, FakePlayer());
        Assert.False(PayloadReader.HasPayload(exe));
        Assert.Equal(FormatProblem.NotARecording, Assert.Throws<RecordingFormatException>(() => PayloadReader.Read(exe)).Problem);
        File.WriteAllBytes(exe, [1, 2, 3]);
        Assert.False(PayloadReader.HasPayload(exe));
        Assert.False(PayloadReader.HasPayload(dir.File("no-existe.exe")));
    }

    [Fact]
    public void Exe_ConLaColaRetocada_EsDaño()
    {
        using var dir = new TempFolder();
        var exe = dir.File("macro.exe");
        ExeBuilder.Build(new MemoryStream(FakePlayer()), Samples.Recording(), new ExeOptions(), exe);
        var bytes = File.ReadAllBytes(exe);

        var longer = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(longer.AsSpan(longer.Length - 16), long.MaxValue / 2);
        Assert.Equal(FormatProblem.Corrupt, Assert.Throws<RecordingFormatException>(() => PayloadReader.Read(new MemoryStream(longer))).Problem);

        var shifted = (byte[])bytes.Clone();
        var length = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(bytes.Length - 16));
        BinaryPrimitives.WriteInt64LittleEndian(shifted.AsSpan(shifted.Length - 16), length + 1);
        Assert.ThrowsAny<RecordingFormatException>(() => PayloadReader.Read(new MemoryStream(shifted)));

        var tampered = (byte[])bytes.Clone();
        tampered[4096 + 40] ^= 0xFF;   // dentro de la grabacion
        Assert.ThrowsAny<RecordingFormatException>(() => PayloadReader.Read(new MemoryStream(tampered)));

        var futureOptions = (byte[])bytes.Clone();
        futureOptions[futureOptions.Length - 16 - ExeOptions.Length] = 9;
        Assert.Equal(FormatProblem.NewerVersion, Assert.Throws<RecordingFormatException>(() => PayloadReader.Read(new MemoryStream(futureOptions))).Problem);
    }

    [Fact]
    public void Compilar_ReproductorQueNoEsExe_NoSeHace()
    {
        using var dir = new TempFolder();
        var exe = dir.File("macro.exe");
        Assert.Throws<InvalidDataException>(() => ExeBuilder.Build(new MemoryStream(new byte[4096]), Samples.Recording(), new ExeOptions(), exe));
        Assert.False(File.Exists(exe));
        Assert.False(File.Exists(exe + ".tmp"));
    }

    [Fact]
    public void Compilar_GrabacionVacia_NoSeHace()
    {
        using var dir = new TempFolder();
        Assert.Equal(FormatProblem.Empty, Assert.Throws<RecordingFormatException>(() =>
            ExeBuilder.Build(new MemoryStream(FakePlayer()), new Recording([]), new ExeOptions(), dir.File("x.exe"))).Problem);
    }

    [Fact]
    public void Opciones_SeLimitan()
    {
        Span<byte> o = stackalloc byte[ExeOptions.Length];
        new ExeOptions { Emergency = (EmergencyKeys)0xFF, CountdownSeconds = 500 }.Write(o);
        var read = ExeOptions.Read(o);
        Assert.Equal(EmergencyKeys.All, read.Emergency);
        Assert.Equal(60, read.CountdownSeconds);
    }

    [Fact]
    public void ReproductorDeVerdad_EncuentraSuGrabacion()
    {
        // CA-07: el exe compilado con el reproductor Native AOT valida su grabacion con --check
        // (sin reproducir nada). Si el reproductor no esta compilado, la prueba no aplica.
        if (RealPlayer() is not { } player)
            return;
        using var dir = new TempFolder();
        var exe = dir.File("macro.exe");
        using (var stream = File.OpenRead(player))
            ExeBuilder.Build(stream, Samples.Recording(), new ExeOptions(), exe);

        var (code, output) = Run(exe, "--check");
        Assert.Equal(0, code);
        Assert.StartsWith($"OK {Samples.AllKinds().Count} ", output);
        Assert.Contains(" Times 7", output);
        Assert.True(new FileInfo(player).Length < 3 * 1024 * 1024, "el reproductor tiene que ser pequeño (RF-30)");

        // El reproductor solo, sin grabacion: codigo 3 y sin aviso con --check.
        var bare = dir.File("bare.exe");
        File.Copy(player, bare);
        Assert.Equal(3, Run(bare, "--check").Code);

        // Con la grabacion dañada: codigo 4.
        var bytes = File.ReadAllBytes(exe);
        bytes[new FileInfo(player).Length + 30] ^= 0x55;
        var broken = dir.File("broken.exe");
        File.WriteAllBytes(broken, bytes);
        Assert.Equal(4, Run(broken, "--check").Code);
    }

    private static (int Code, string Output) Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(20_000));
        return (p.ExitCode, output.Trim());
    }
}
