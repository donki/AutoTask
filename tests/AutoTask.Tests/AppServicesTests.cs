using Microsoft.Win32;
using SocAutoTask.AppServices;
using SocAutoTask.Editing;
using SocAutoTask.Format;
using SocAutoTask.Input;
using SocAutoTask.Localization;
using SocAutoTask.Model;

namespace SocAutoTask.Tests;

/// <summary>Ajustes, rutas, sesion, instancia unica, asociacion, errores de fichero y entradas (RF-25..27, RF-48..50).</summary>
public sealed class AppServicesTests
{
    [Fact]
    public void Rutas_PortableSiHayPortableIni()
    {
        var portable = AppPaths.Resolve(@"D:\Apps\AutoTask", @"C:\Users\x\AppData\Local", p => p == @"D:\Apps\AutoTask\portable.ini");
        Assert.True(portable.Portable);
        Assert.Equal(@"D:\Apps\AutoTask", portable.DataFolder);
        Assert.Equal(@"D:\Apps\AutoTask\settings.json", portable.SettingsFile);

        var normal = AppPaths.Resolve(@"D:\Apps\AutoTask", @"C:\Users\x\AppData\Local", _ => false);
        Assert.False(normal.Portable);
        Assert.Equal(@"C:\Users\x\AppData\Local\sOCAutoTask", normal.DataFolder);
        Assert.EndsWith("recuperada.soctask", normal.RecoveryFile);
    }

    [Fact]
    public void Ajustes_GuardarYLeer()
    {
        using var dir = new TempFolder();
        var path = dir.File("settings.json");
        var settings = new AppSettings
        {
            Language = "es",
            Theme = AppTheme.Dark,
            RecordHotkey = "Ctrl+F9",
            Emergency = EmergencyKeys.Pause,
            CountdownSeconds = 3,
            RecordKeyboard = false,
            DefaultPlayback = new PlaybackOptions { Speed = 4, Repeat = RepeatMode.Continuous },
            GuideShown = true,
            LastSeenVersion = "2026.10.01.0",
            WindowLeft = -1500,
        };
        settings.AddRecent(@"C:\a.soctask");
        settings.Save(path);
        var read = AppSettings.Load(path);
        Assert.Equal("es", read.Language);
        Assert.Equal(AppTheme.Dark, read.Theme);
        Assert.Equal(new Hotkey(HotkeyModifiers.Ctrl, 0x78), read.RecordKey);
        Assert.Equal(EmergencyKeys.Pause, read.Emergency);
        Assert.Equal(3, read.CountdownSeconds);
        Assert.False(read.RecordKeyboard);
        Assert.Equal(settings.DefaultPlayback, read.DefaultPlayback);
        Assert.Equal([@"C:\a.soctask"], read.Recent);
        Assert.True(read.GuideShown);
        Assert.Equal(-1500, read.WindowLeft);
        Assert.Contains("\"Dark\"", File.ReadAllText(path));   // enumerados legibles
    }

    [Fact]
    public void Ajustes_IlegiblesONoExisten_LosDeFabrica()
    {
        using var dir = new TempFolder();
        Assert.Equal(Hotkey.DefaultRecord, AppSettings.Load(dir.File("no.json")).RecordKey);
        File.WriteAllText(dir.File("roto.json"), "{ esto no es json");
        var broken = AppSettings.Load(dir.File("roto.json"));
        Assert.True(broken.RecordKeyboard);
        Assert.Equal(EmergencyKeys.All, broken.Emergency);
    }

    [Fact]
    public void Ajustes_SeLlevanASusLimites()
    {
        var s = new AppSettings
        {
            CountdownSeconds = 99,
            EscapeHoldMs = 5,
            Emergency = (EmergencyKeys)0xFF,
            RecordHotkey = "nada",
            PlayHotkey = "Ctrl+Alt+Shift+R",
            Language = "fr",
            DefaultPlayback = new PlaybackOptions { Speed = 0 },
            Recent = Enumerable.Range(0, 20).Select(i => $"f{i}").ToList(),
        };
        s.Normalize();
        Assert.Equal(10, s.CountdownSeconds);
        Assert.Equal(200, s.EscapeHoldMs);
        Assert.Equal(EmergencyKeys.All, s.Emergency);
        Assert.Equal(Hotkey.DefaultRecord, s.RecordKey);
        Assert.Equal(Hotkey.DefaultPlay, s.PlayKey);   // no puede ser igual que el de grabar
        Assert.Null(s.Language);
        Assert.Equal(PlaybackOptions.MinSpeed, s.DefaultPlayback.Speed);
        Assert.Equal(AppSettings.MaxRecent, s.Recent.Count);
    }

    [Fact]
    public void Recientes_SinRepetir_YLosQueYaNoEstan()
    {
        var s = new AppSettings();
        for (var i = 0; i < 10; i++)
            s.AddRecent($@"C:\f{i}.soctask");
        s.AddRecent(@"c:\F3.SOCTASK");
        Assert.Equal(AppSettings.MaxRecent, s.Recent.Count);
        Assert.Equal(@"c:\F3.SOCTASK", s.Recent[0]);
        Assert.Single(s.Recent, p => p.Equals(@"C:\f3.soctask", StringComparison.OrdinalIgnoreCase));
        Assert.True(s.PruneRecent(p => p.Contains('9')));
        Assert.Equal([@"C:\f9.soctask"], s.Recent);
        Assert.False(s.PruneRecent(_ => true));
    }

    [Fact]
    public void Sesion_SinGuardar_YNombres()
    {
        try
        {
            Loc.Use("es");
            var session = new MacroSession();
            var changes = 0;
            session.Changed += () => changes++;
            Assert.False(session.HasRecording);
            Assert.Equal("Sin título", session.DisplayName);
            Assert.Equal("grabacion.soctask", session.SuggestedName(".soctask"));
            Assert.Null(session.SuggestedFolder);

            session.SetRecorded(Samples.Recording());
            Assert.True(session.Dirty);
            Assert.True(session.HasRecording);

            session.MarkSaved(@"C:\macros\formulario.soctask");
            Assert.False(session.Dirty);
            Assert.Equal("formulario", session.DisplayName);
            Assert.Equal("formulario.exe", session.SuggestedName(".exe"));
            Assert.Equal(@"C:\macros", session.SuggestedFolder);

            session.SetOptions(session.Recording!.Options);
            Assert.False(session.Dirty);   // las mismas opciones no ensucian
            session.SetOptions(session.Recording.Options with { Speed = 10 });
            Assert.True(session.Dirty);
            Assert.Equal(10, session.Recording.Options.Speed);

            session.SetOpened(Samples.Recording(), @"C:\otra\vieja.rec", LoadedKind.Rec);
            Assert.True(session.Dirty);   // importada: aun no es un .soctask
            Assert.Null(session.FilePath);
            Assert.Equal("vieja", session.DisplayName);
            Assert.Equal("vieja.soctask", session.SuggestedName(".soctask"));

            session.SetOpened(Samples.Recording(), @"C:\otra\buena.soctask", LoadedKind.SocTask);
            Assert.False(session.Dirty);
            session.ReplaceEvents([MacroEvent.Move(1, 1)]);
            Assert.True(session.Dirty);
            Assert.Equal(1, session.Recording!.Count);

            session.Clear();
            Assert.False(session.HasRecording);
            Assert.Equal(7, changes);
        }
        finally
        {
            Loc.Use("en");
        }
    }

    [Fact]
    public void Sesion_SinGrabacion_IgnoraCambios()
    {
        var session = new MacroSession();
        session.ReplaceEvents([MacroEvent.Move(1, 1)]);
        session.SetOptions(new PlaybackOptions { Speed = 3 });
        Assert.Null(session.Recording);
        Assert.False(session.Dirty);
    }

    [Theory]
    [InlineData("2026.10.01.0", "2026.9.30.0", InstanceDecision.TakeOver)]
    [InlineData("2026.10.01.1", "2026.10.01.0", InstanceDecision.TakeOver)]
    [InlineData("2026.10.01.0", "2026.10.1.0", InstanceDecision.HandOff)]
    [InlineData("2026.10.01.0", "2026.10.02.0", InstanceDecision.HandOff)]
    [InlineData("2026.10.01.0", "basura", InstanceDecision.HandOff)]
    public void InstanciaUnica_ManLaVersionNueva(string mine, string theirs, InstanceDecision expected) =>
        Assert.Equal(expected, SingleInstance.Decide(mine, theirs));

    [Fact]
    public void InstanciaUnica_LaSegundaDeLaMismaVersion_PasaElTurnoYElFichero()
    {
        var suffix = ".tests." + Guid.NewGuid().ToString("N");
        using var first = new SingleInstance("2026.10.01.0", suffix);
        string? shown = null;
        using var got = new ManualResetEventSlim();
        first.ShowRequested += f => { shown = f; got.Set(); };
        Assert.True(first.Claim(string.Empty));

        // La segunda en otro hilo (el mutex es por hilo).
        bool? secondContinues = null;
        var t = new Thread(() =>
        {
            using var second = new SingleInstance("2026.10.01.0", suffix);
            secondContinues = second.Claim(@"C:\macros\x.soctask");
        });
        t.Start();
        Assert.True(t.Join(15_000));
        Assert.False(secondContinues);
        Assert.True(got.Wait(5000));
        Assert.Equal(@"C:\macros\x.soctask", shown);
    }

    [Fact]
    public void InstanciaUnica_Sola_Sigue()
    {
        using var only = new SingleInstance("2026.10.01.0", ".tests." + Guid.NewGuid().ToString("N"));
        Assert.True(only.Claim(string.Empty));
    }

    [Fact]
    public void Asociacion_Entradas()
    {
        var entries = FileAssociation.Entries(@"C:\Apps\sOCAutoTask.exe", "Grabación");
        Assert.Equal(4, entries.Count);
        Assert.Contains(entries, e => e.Key == @"Software\Classes\.soctask" && e.Value == FileAssociation.ProgId);
        Assert.Contains(entries, e => e.Key.EndsWith(@"shell\open\command") && e.Value == "\"C:\\Apps\\sOCAutoTask.exe\" \"%1\"");
        Assert.All(entries, e => Assert.StartsWith(@"Software\Classes\", e.Key));
    }

    [Fact]
    public void Asociacion_PonerComprobarYQuitar_EnUnaRamaDePruebas()
    {
        var branch = @"Software\sOCAutoTask.Tests\" + Guid.NewGuid().ToString("N");
        using var root = Registry.CurrentUser.CreateSubKey(branch);
        try
        {
            var exe = @"C:\Apps\sOCAutoTask.exe";
            Assert.False(FileAssociation.IsApplied(exe, root));
            FileAssociation.Apply(exe, "Grabación", root);
            Assert.True(FileAssociation.IsApplied(exe, root));
            Assert.False(FileAssociation.IsApplied(@"C:\Otra\sOCAutoTask.exe", root));
            FileAssociation.Remove(root);
            Assert.False(FileAssociation.IsApplied(exe, root));
            Assert.Null(root.OpenSubKey($@"Software\Classes\{FileAssociation.ProgId}"));

            // Si .soctask es de otro programa, quitar no la toca.
            using (var ext = root.CreateSubKey(@"Software\Classes\.soctask"))
                ext.SetValue(string.Empty, "Otro.Programa");
            FileAssociation.Remove(root);
            using var still = root.OpenSubKey(@"Software\Classes\.soctask");
            Assert.Equal("Otro.Programa", still?.GetValue(null));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(branch, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKey(@"Software\sOCAutoTask.Tests", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void ErroresDeFichero_TienenSuTexto()
    {
        Assert.Equal("FileNoPermission", FileErrors.LocKey(new UnauthorizedAccessException()));
        Assert.Equal("FileNotFound", FileErrors.LocKey(new FileNotFoundException()));
        Assert.Equal("FileNotFound", FileErrors.LocKey(new DirectoryNotFoundException()));
        Assert.Equal("FilePathTooLong", FileErrors.LocKey(new PathTooLongException()));
        Assert.Equal("FileDiskFull", FileErrors.LocKey(new IOException("lleno", unchecked((int)0x80070070))));
        Assert.Equal("FileInUse", FileErrors.LocKey(new IOException("en uso", unchecked((int)0x80070020))));
        Assert.Equal("FormatCorrupt", FileErrors.LocKey(new InvalidDataException()));
        Assert.Equal("FileGenericError", FileErrors.LocKey(new IOException("otra")));
        Assert.Equal("FormatNewerVersion", FileErrors.LocKey(new RecordingFormatException(FormatProblem.NewerVersion, "")));
        foreach (var key in new[] { "FileNoPermission", "FileNotFound", "FilePathTooLong", "FileDiskFull", "FileInUse", "FormatCorrupt", "FileGenericError" })
            Assert.NotEmpty(Loc.Spanish[key]);
    }

    [Theory]
    [InlineData("2,5", 2.5)]
    [InlineData("2.5", 2.5)]
    [InlineData(" 10 ", 10)]
    [InlineData("4×", 4)]
    [InlineData("4x", 4)]
    public void Numeros_ConComaOPunto(string text, double expected)
    {
        Assert.True(InputParsing.TryNumber(text, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Numeros_NoValidos(string? text) => Assert.False(InputParsing.TryNumber(text, out _));

    [Fact]
    public void Reproduccion_DesdeLaInterfaz()
    {
        Assert.Null(InputParsing.TryPlayback(2, null, RepeatMode.Once, "5", "1,5", out var o));
        Assert.Equal(new PlaybackOptions { Speed = 2, Repeat = RepeatMode.Once, Times = 5, PauseBetweenMs = 1500 }, o);
        Assert.Null(InputParsing.TryPlayback(null, "0,1", RepeatMode.Times, "100000", "", out o));
        Assert.Equal(0.1, o.Speed);
        Assert.Equal(100000, o.Times);
        Assert.Equal("InvalidSpeed", InputParsing.TryPlayback(null, "0,05", RepeatMode.Once, "1", "0", out _));
        Assert.Equal("InvalidSpeed", InputParsing.TryPlayback(null, "1001", RepeatMode.Once, "1", "0", out _));
        Assert.Equal("InvalidTimes", InputParsing.TryPlayback(1, null, RepeatMode.Times, "0", "0", out _));
        Assert.Equal("InvalidTimes", InputParsing.TryPlayback(1, null, RepeatMode.Times, "dos", "0", out _));
        Assert.Null(InputParsing.TryPlayback(1, null, RepeatMode.Continuous, "dos", "0", out o));   // N no se usa: da igual
        Assert.Equal(1, o.Times);
        Assert.Equal("InvalidPause", InputParsing.TryPlayback(1, null, RepeatMode.Once, "1", "-1", out _));
        Assert.Equal("InvalidPause", InputParsing.TryPlayback(1, null, RepeatMode.Once, "1", "3601", out _));
    }

    [Fact]
    public void Segundos()
    {
        Assert.True(InputParsing.TrySeconds("3", 0, 10, out var s));
        Assert.Equal(3, s);
        Assert.True(InputParsing.TrySeconds("", 0, 10, out s));
        Assert.Equal(0, s);
        Assert.False(InputParsing.TrySeconds("", 1, 10, out _));
        Assert.False(InputParsing.TrySeconds("11", 0, 10, out _));
        Assert.True(InputParsing.TrySecondsToMs("1,5", 0.2, 10, out var ms));
        Assert.Equal(1500, ms);
        Assert.False(InputParsing.TrySecondsToMs("0,1", 0.2, 10, out _));
        Assert.Equal("1,5", InputParsing.Show(1.5, System.Globalization.CultureInfo.GetCultureInfo("es-ES")));
    }

    [Theory]
    [InlineData(new string[0], null, false, null)]
    [InlineData(new[] { "--play" }, null, true, null)]
    [InlineData(new[] { "--wait-pid", "1234" }, null, false, 1234)]
    [InlineData(new[] { "--wait-pid", "x" }, null, false, null)]
    [InlineData(new[] { "--otra" }, null, false, null)]
    public void LineaDeOrdenes_Opciones(string[] args, string? file, bool play, int? pid)
    {
        var (f, p, w) = CommandLine.Parse(args);
        Assert.Equal(file, f);
        Assert.Equal(play, p);
        Assert.Equal(pid, w);
    }

    [Fact]
    public void LineaDeOrdenes_Fichero()
    {
        var (f, p, _) = CommandLine.Parse([@"C:\macros\a.soctask", "--play", @"C:\otro.soctask"]);
        Assert.Equal(@"C:\macros\a.soctask", f);
        Assert.True(p);
    }

    [Fact]
    public void Novedades_SoloAlEstrenarVersion()
    {
        Assert.False(WhatsNew.ShouldShow("", "2026.10.01.0"));   // primera instalacion: sale la guia
        Assert.False(WhatsNew.ShouldShow("2026.10.01.0", "2026.10.01.0"));
        Assert.True(WhatsNew.ShouldShow("2026.9.30.0", "2026.10.01.0"));
        Assert.Equal(AppInfo.Version, WhatsNew.Entries[0].Version);   // la de arriba es la actual
        Assert.All(WhatsNew.Entries, e => Assert.Equal(e.Spanish.Length, e.English.Length));
        Assert.True(WhatsNew.Latest().Count() <= WhatsNew.Shown);
    }

    [Fact]
    public void Version_ConservaLosCeros()
    {
        Assert.Matches(@"^\d{4}\.\d{2}\.\d{2}\.\d+$", AppInfo.Version);
        Assert.False(AppInfo.IsPackaged);
    }

    [Fact]
    public void Registro_EscribeYRota()
    {
        using var dir = new TempFolder();
        var before = AppPaths.Current;
        try
        {
            AppPaths.Current = AppPaths.At(dir.Path);
            AppLog.Write("primera linea");
            Assert.Contains("primera linea", File.ReadAllText(AppLog.FilePath));
            File.WriteAllText(AppLog.FilePath, new string('x', 1024 * 1024 + 10));
            AppLog.Write("tras rotar");
            Assert.True(File.Exists(Path.Combine(dir.Path, "errors.old.log")));
            Assert.Contains("tras rotar", File.ReadAllText(AppLog.FilePath));
            Assert.True(new FileInfo(AppLog.FilePath).Length < 1000);
        }
        finally
        {
            AppPaths.Current = before;
        }
    }

    [Fact]
    public void Describir_Eventos_YTiempos()
    {
        try
        {
            Loc.Use("es");
            Assert.Equal("Botón pulsado", EventDescriber.Kind(MacroEvent.Down(MouseButton.Left, 1, 2)));
            Assert.Equal("izquierdo en 1, 2", EventDescriber.Detail(MacroEvent.Down(MouseButton.Left, 1, 2)));
            Assert.Equal("a -5, 7", EventDescriber.Detail(MacroEvent.Move(-5, 7)));
            Assert.Equal("-120 en 3, 4", EventDescriber.Detail(MacroEvent.WheelAt(3, 4, -120)));
            Assert.Equal("Mayús izq.", EventDescriber.Detail(MacroEvent.Key(true, 0xA0)));
            Assert.Equal("A", EventDescriber.Detail(MacroEvent.Key(false, 0x41)));
            Assert.Equal("1,5 s", EventDescriber.Detail(MacroEvent.WaitFor(1500)));
            Assert.Equal("850 ms", EventDescriber.FormatMs(850));
            Assert.Equal("12,4 s", EventDescriber.FormatMs(12_400));
            Assert.Equal("3 min 05 s", EventDescriber.FormatMs(185_000));
            Assert.Equal("1 h 02 min", EventDescriber.FormatMs(3_720_000));
            Assert.Equal("0,5×", EventDescriber.Speed(0.5));
            Loc.Use("en");
            Assert.Equal("0.5×", EventDescriber.Speed(0.5));
            Assert.Equal("left at 1, 2", EventDescriber.Detail(MacroEvent.Down(MouseButton.Left, 1, 2)));
            Assert.Equal("0:42", EventDescriber.Clock(41_200));
            Assert.Equal("1:02:03", EventDescriber.Clock(3_723_000));
            Assert.Equal("0:00", EventDescriber.Clock(-5));
        }
        finally
        {
            Loc.Use("en");
        }
    }
}
