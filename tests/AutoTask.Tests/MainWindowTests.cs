using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using SocAutoTask.AppServices;
using SocAutoTask.Compile;
using SocAutoTask.Desktop;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Editing;
using SocAutoTask.Format;
using SocAutoTask.Input;
using SocAutoTask.Localization;
using SocAutoTask.Model;
using SocAutoTask.Playback;

namespace SocAutoTask.Tests;

/// <summary>La barra principal con la plataforma falsa: grabar, reproducir, ficheros, compilar, menu.</summary>
public sealed class MainWindowTests : UiTest
{
    internal static List<MacroEvent> Events() =>
    [
        MacroEvent.Move(10, 10),
        MacroEvent.Down(MouseButton.Left, 10, 10, 50),
        MacroEvent.Up(MouseButton.Left, 10, 10, 40),
        MacroEvent.Key(true, 0x41, 0x1E, delay: 30),
        MacroEvent.Key(false, 0x41, 0x1E, delay: 20),
    ];

    private string SavedFile(string name = "macro.soctask", PlaybackOptions? options = null, ScreenRect screen = default)
    {
        var path = Temp.File(name);
        RecordingFile.Save(new Recording(Events(), options ?? new PlaybackOptions(), screen), path);
        return path;
    }

    private static string Status(MainWindow w) => Ui.Find<TextBlock>(w, "StatusText").Text;

    private static string Info(MainWindow w) => Ui.Find<TextBlock>(w, "InfoText").Text;

    private static Button Button(MainWindow w, string name) => Ui.Find<Button>(w, name);

    private static bool Idle(MainWindow w) => Ui.Field<object>(w, "_state").ToString() == "Idle";

    private MainWindow Recorded(AppSettings? settings = null)
    {
        Fake.Recorder.Events = Events();
        var w = new MainWindow(settings ?? Settings());
        w.ToggleRecording();
        w.ToggleRecording();
        Assert.True(w.Session.HasRecording);
        return w;
    }

    private static MenuItem MenuItem(ContextMenu menu, string id) =>
        menu.Items.OfType<MenuItem>().SingleOrDefault(i => System.Windows.Automation.AutomationProperties.GetAutomationId(i) == id)
        ?? throw new InvalidOperationException(id);

    private static bool HasMenuItem(ContextMenu menu, string id) =>
        menu.Items.OfType<MenuItem>().Any(i => System.Windows.Automation.AutomationProperties.GetAutomationId(i) == id);

    // ------------------------------------------------------------ arranque

    [Fact]
    public void AlAbrir_SinGrabacion_SoloSePuedeGrabarYAbrir()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            Assert.Equal(Loc.Get("StatusReady"), Status(w));
            Assert.Equal(Loc.Get("InfoEmpty"), Info(w));
            Assert.False(Button(w, "PlayButton").IsEnabled);
            Assert.False(Button(w, "SaveButton").IsEnabled);
            Assert.False(Button(w, "CompileButton").IsEnabled);
            Assert.False(Button(w, "EditorButton").IsEnabled);
            Assert.True(Button(w, "RecordButton").IsEnabled);
            Assert.True(Button(w, "OpenButton").IsEnabled);
            Assert.Equal(WindowStartupLocation.CenterScreen, w.WindowStartupLocation);
            Assert.Equal(Loc.Format("RecordTip", Settings().RecordKey.Display()), Button(w, "RecordButton").ToolTip);
        });
    }

    [Fact]
    public void Posicion_GuardadaDentroDeLaPantalla_SeRespeta_YFuera_SeCentra()
    {
        Ui.Run(() =>
        {
            var inside = Settings();
            inside.WindowLeft = SystemParameters.VirtualScreenLeft + 10;
            inside.WindowTop = SystemParameters.VirtualScreenTop + 20;
            var w = new MainWindow(inside);
            Assert.Equal(WindowStartupLocation.Manual, w.WindowStartupLocation);
            Assert.Equal(inside.WindowLeft, w.Left);
            Assert.Equal(inside.WindowTop, w.Top);

            var outside = Settings();
            outside.WindowLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 500;
            outside.WindowTop = 0;
            Assert.Equal(WindowStartupLocation.CenterScreen, new MainWindow(outside).WindowStartupLocation);
        });
    }

    [Fact]
    public void AjustesDeVentana_SiempreEncima_YEtiquetas()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            settings.AlwaysOnTop = true;
            settings.ShowLabels = false;
            var w = new MainWindow(settings);
            Assert.True(w.Topmost);
            Assert.Equal(Visibility.Collapsed, Application.Current.Resources["LabelVisibility"]);
            settings.ShowLabels = true;
            settings.AlwaysOnTop = false;
            w.ApplyWindowSettings();
            Assert.False(w.Topmost);
            Assert.Equal(Visibility.Visible, Application.Current.Resources["LabelVisibility"]);
        });
    }

    [Fact]
    public void CambiarDeIdioma_RetraduceLaBarra_YAlCerrarSeDesengancha()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            Loc.Use("en");
            Assert.Equal(Loc.Get("StatusReady"), Status(w));
            Assert.Equal(Loc.Get("Record"), Button(w, "RecordButton").Content);
            Assert.Equal(Loc.Get("InfoEmpty"), Info(w));
            w.Close();
            Loc.Use("es");
            Assert.NotEqual(Loc.Get("StatusReady"), Status(w));   // ya no escucha
        });
    }

    // ------------------------------------------------------------ grabar

    [Fact]
    public void Grabar_YParar_DejaLaGrabacionEnLaSesion()
    {
        Fake.Recorder.Events = Events();
        Ui.Run(() =>
        {
            var settings = Settings();
            settings.RecordKeyboard = false;
            var w = new MainWindow(settings);
            Ui.Click(Button(w, "RecordButton"));
            Assert.True(Fake.Recorder.Started);
            Assert.False(Fake.RecorderOptions[0].Keyboard);
            Assert.Equal("stop", Button(w, "RecordButton").Tag);
            Assert.Equal(Loc.Get("StopShort"), Button(w, "RecordButton").Content);
            Assert.False(Button(w, "PlayButton").IsEnabled);
            Assert.False(Button(w, "OpenButton").IsEnabled);
            Assert.Contains("5", Status(w));
            Ui.Click(Button(w, "RecordButton"));
            Assert.True(Fake.Recorder.Disposed);
            Assert.True(w.Session.HasRecording);
            Assert.True(w.Session.Dirty);
            Assert.Equal(Loc.Format("StatusRecorded", 5, EventDescriber.FormatMs(140)), Status(w));
            Assert.True(Button(w, "PlayButton").IsEnabled);
            Assert.True(Button(w, "SaveButton").IsEnabled);
            Assert.Contains("*", Info(w));
            Assert.Contains(Loc.Get("RepeatOnceShort"), Info(w));
        });
    }

    [Fact]
    public void Grabar_SinNada_LoDice()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.ToggleRecording();
            w.ToggleRecording();
            Assert.False(w.Session.HasRecording);
            Assert.Equal(Loc.Get("StatusNothingRecorded"), Status(w));
        });
    }

    [Fact]
    public void Grabar_SiWindowsNoDejaPonerLosGanchos_AvisaYSigueParado()
    {
        Fake.Recorder.StartError = new Win32Exception(5);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            w.ToggleRecording();
            Assert.Equal(Loc.Get("RecordFailed"), message);
            Assert.True(Idle(w));
            Assert.Null(Ui.Field<object?>(w, "_recorder"));
        });
        Assert.Contains("no se pudo empezar a grabar", File.ReadAllText(AppLog.FilePath));
    }

    [Fact]
    public void Grabar_EnModoAislado_NoGraba()
    {
        Ui.Run(() =>
        {
            Sandbox.IsOn = true;
            var w = new MainWindow(Settings());
            Assert.EndsWith("[SOC_SANDBOX]", w.Title);
            Assert.True(w.HotkeysOk);
            w.ToggleRecording();
            Assert.False(Fake.Recorder.Started);
            Assert.Equal(Loc.Get("SandboxNoRecord"), Status(w));
        });
    }

    [Fact]
    public void Grabar_ConCambiosSinGuardar_SiSeCancela_NoEmpieza()
    {
        Ui.Run(() =>
        {
            var w = Recorded();
            var recorder = Fake.Recorder = new FakeRecorder();
            Ui.Answer("CancelButton");
            w.ToggleRecording();
            Assert.False(recorder.Started);
            Assert.True(Idle(w));
        });
    }

    [Fact]
    public void Grabar_ConUnaVentanaDeAdministrador_OfreceReiniciarElevado()
    {
        Fake.Recorder.Events = Events();
        Fake.ForegroundElevated = true;
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.ToggleRecording();
            string? offer = null;
            Ui.Answer("OkButton", m => offer = m);         // «¿reiniciar como administrador?»
            Ui.Answer("DiscardButton");                     // lo grabado no se guarda
            w.ToggleRecording();
            Assert.Equal(Loc.Get("ElevatedWhileRecording"), offer);
            var start = Assert.Single(Fake.Started);
            Assert.Equal("runas", start.Verb);
            Assert.Equal(Fake.ExePath, start.FileName);
            Assert.Equal($"--wait-pid {Environment.ProcessId}", start.Arguments);
            Assert.True(Ui.IsClosed(w));
        });
    }

    [Fact]
    public void Grabar_SiYaSeEsAdministrador_NoOfreceNada()
    {
        Fake.Recorder.Events = Events();
        Fake.ForegroundElevated = true;
        Fake.Elevated = true;
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.ToggleRecording();
            w.ToggleRecording();
            Assert.Empty(Fake.Started);
        });
    }

    // ------------------------------------------------------------ reproducir

    [Fact]
    public void Reproducir_EnviaTodo_YAcaba()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(SavedFile());
            Ui.Click(Button(w, "PlayButton"));
            Ui.WaitFor(() => Idle(w));
            Assert.Equal(5, Fake.Sink.Sent.Count);
            Assert.True(Fake.Emergency.Started);
            Assert.True(Fake.Emergency.Disposed);
            Assert.Equal(EmergencyKeys.All, Fake.EmergencyArgs!.Value.Keys);
            Assert.Equal(Loc.Get("StatusFinished"), Status(w));
            Assert.Equal("play", Button(w, "PlayButton").Tag);
        });
    }

    [Fact]
    public void Reproducir_Continuo_SeParaConElBoton()
    {
        Fake.Clock.Advanced = _ => Thread.Sleep(1);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(SavedFile(options: new PlaybackOptions { Repeat = RepeatMode.Continuous }));
            Assert.Contains(Loc.Get("RepeatContinuousShort"), Info(w));
            w.TogglePlayback();
            Assert.Equal("stop", Button(w, "PlayButton").Tag);
            Assert.False(Button(w, "RecordButton").IsEnabled);
            Ui.WaitFor(() => Fake.Sink.Sent.Count > 20);
            Ui.WaitFor(() => Status(w).Contains(Loc.Format("LoopContinuous", 1)[..4]));
            w.TogglePlayback();
            Ui.WaitFor(() => Idle(w));
            Assert.Equal(Loc.Get("StatusStopped"), Status(w));
        });
    }

    [Fact]
    public void Reproducir_LaParadaDeEmergencia_LoDice()
    {
        Fake.Clock.Advanced = _ => Thread.Sleep(1);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(SavedFile(options: new PlaybackOptions { Repeat = RepeatMode.Continuous }));
            w.TogglePlayback();
            Ui.WaitFor(() => Fake.Sink.Sent.Count > 5);
            Fake.EmergencyArgs!.Value.Stop();
            Ui.WaitFor(() => Idle(w));
            Assert.Equal(Loc.Get("StatusEmergency"), Status(w));
        });
    }

    [Fact]
    public void Reproducir_SinParadaDeEmergencia_NoLaArranca()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            settings.Emergency = EmergencyKeys.None;
            var w = new MainWindow(settings);
            Assert.Contains(Loc.Get("EmergencyNone"), (string)Button(w, "PlayButton").ToolTip);
            w.OpenFile(SavedFile());
            w.TogglePlayback();
            Ui.WaitFor(() => Idle(w));
            Assert.False(Fake.Emergency.Started);
            Assert.Equal(5, Fake.Sink.Sent.Count);
        });
    }

    [Fact]
    public void Reproducir_SiNoSePuedePonerLaParadaDeEmergencia_NoReproduce()
    {
        Fake.Emergency.StartError = new Win32Exception(5);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(SavedFile());
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            w.TogglePlayback();
            Assert.Equal(Loc.Get("EmergencyFailed"), message);
            Assert.True(Idle(w));
            Assert.Empty(Fake.Sink.Sent);
        });
    }

    [Fact]
    public void Reproducir_EnModoAislado_NoMueveNada()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(SavedFile());
            Sandbox.IsOn = true;
            w.TogglePlayback();
            Assert.Equal(Loc.Get("SandboxNoPlay"), Status(w));
            Assert.Empty(Fake.Sink.Sent);
        });
    }

    [Fact]
    public void Reproducir_SinGrabacion_NoHaceNada()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.TogglePlayback();
            Assert.Null(Fake.EmergencyArgs);
        });
    }

    [Fact]
    public void Reproducir_SiWindowsRechazaEventos_OfreceElevar()
    {
        Fake.Sink.OnSend = _ => Fake.Sink.Rejected++;
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(SavedFile());
            string? offer = null;
            Ui.Answer("CancelButton", m => offer = m);
            w.TogglePlayback();
            Ui.WaitFor(() => offer is not null);
            Assert.Equal(Loc.Get("ElevatedWhilePlaying"), offer);
            Assert.Empty(Fake.Started);
        });
        Assert.Contains("SendInput rechazo 5 eventos", File.ReadAllText(AppLog.FilePath));
    }

    [Fact]
    public void Reproducir_ConUnaVentanaDeAdministradorDelante_TambienOfreceElevar()
    {
        Fake.ForegroundElevated = true;
        Fake.Clock.Advanced = _ => Thread.Sleep(2);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(SavedFile(options: new PlaybackOptions { Repeat = RepeatMode.Times, Times = 30 }));
            w.TogglePlayback();
            Ui.Call(w, "Tick");
            Assert.True(Ui.Field<bool>(w, "_elevatedWarning"));
            Ui.Answer("CancelButton");
            Ui.WaitFor(() => Idle(w));
        });
        Assert.Contains("ventana elevada", File.ReadAllText(AppLog.FilePath));
    }

    [Fact]
    public void EstadoDeLaReproduccion_CadaFase()
    {
        Fake.Clock.Advanced = _ => Thread.Sleep(1);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            Ui.Call(w, "ShowPlaybackStatus", new PlaybackStatus { Phase = PlaybackPhase.Countdown, CountdownSeconds = 3 }, false);
            Assert.Equal(Loc.Get("StatusReady"), Status(w));   // parado: no cambia nada

            w.OpenFile(SavedFile(options: new PlaybackOptions { Repeat = RepeatMode.Continuous }));
            w.TogglePlayback();
            void Show(PlaybackStatus s, bool changed = false) => Ui.Call(w, "ShowPlaybackStatus", s, changed);
            Show(new PlaybackStatus { Phase = PlaybackPhase.WaitingForKeys });
            Assert.Equal(Loc.Get("StatusReleaseKeys"), Status(w));
            Show(new PlaybackStatus { Phase = PlaybackPhase.Countdown, CountdownSeconds = 3 }, changed: true);
            Assert.Equal(Loc.Format("StatusCountdown", 3) + " · " + Loc.Get("ScreenChanged"), Status(w));
            Show(new PlaybackStatus { Phase = PlaybackPhase.PauseBetweenLoops, Loop = 2, Loops = 5 });
            Assert.Equal(Loc.Format("StatusPause", Loc.Format("LoopOf", 2, 5)), Status(w));
            Show(new PlaybackStatus { Phase = PlaybackPhase.Playing, Loop = 1, Loops = 2, RemainingLoopMs = 1500, RemainingTotalMs = 4000 });
            Assert.Equal(Loc.Format("StatusPlaying", Loc.Format("LoopOf", 1, 2), EventDescriber.Clock(1500), EventDescriber.Clock(4000)), Status(w));
            Show(new PlaybackStatus { Phase = PlaybackPhase.Playing, Loop = 7, RemainingLoopMs = 100 });
            Assert.Equal(Loc.Format("StatusPlayingContinuous", Loc.Format("LoopContinuous", 7), EventDescriber.Clock(100)), Status(w));
            var before = Status(w);
            Show(new PlaybackStatus { Phase = PlaybackPhase.Finished }, changed: true);
            Assert.Equal(before, Status(w));
            w.TogglePlayback();
            Ui.WaitFor(() => Idle(w));
        });
    }

    [Fact]
    public void Reproducir_OtraPantalla_LoAvisa()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(SavedFile(options: new PlaybackOptions { Repeat = RepeatMode.Times, Times = 3 }, screen: new ScreenRect(-99999, 0, 10, 10)));
            Assert.Contains("3", Info(w));
            w.TogglePlayback();
            Ui.WaitFor(() => Idle(w));
        });
    }

    [Fact]
    public void Cerrar_MientrasReproduce_ParaYSeCierra()
    {
        Fake.Clock.Advanced = _ => Thread.Sleep(1);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(SavedFile(options: new PlaybackOptions { Repeat = RepeatMode.Continuous }));
            w.Show();
            w.TogglePlayback();
            Ui.WaitFor(() => Fake.Sink.Sent.Count > 3);
            w.Close();
            Assert.True(Ui.IsClosed(w));
            Assert.True(Fake.Emergency.Disposed || Ui.Field<object?>(w, "_emergency") is not null);
        });
    }

    [Fact]
    public void Cerrar_MientrasGraba_ParaYPreguntaPorLoGrabado()
    {
        Fake.Recorder.Events = Events();
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.Show();
            w.ToggleRecording();
            Ui.Answer("CancelButton");
            w.Close();
            Assert.False(Ui.IsClosed(w));           // se cancelo el cierre
            Assert.True(w.Session.HasRecording);
            Ui.Answer("DiscardButton");
            w.Close();
            Assert.True(Ui.IsClosed(w));
        });
    }

    [Fact]
    public void Cerrar_GuardaPosicionYAjustes()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            var w = new MainWindow(settings);
            w.Show();
            w.Left = 123;
            w.Top = 45;
            w.Close();
            Assert.Equal(123, settings.WindowLeft);
            Assert.Equal(45, settings.WindowTop);
        });
        Assert.True(File.Exists(AppPaths.Current.SettingsFile));
    }

    // ------------------------------------------------------------ ficheros

    [Fact]
    public void Abrir_ConElDialogo_LaCargaYLaApuntaEnRecientes()
    {
        var path = SavedFile();
        Fake.OpenAnswers.Enqueue(path);
        Fake.OpenAnswers.Enqueue(null);
        Ui.Run(() =>
        {
            var settings = Settings();
            var w = new MainWindow(settings);
            Ui.Click(Button(w, "OpenButton"));
            Assert.Equal(path, w.Session.FilePath);
            Assert.False(w.Session.Dirty);
            Assert.Equal(Loc.Format("StatusOpened", "macro.soctask"), Status(w));
            Assert.Equal(path, Assert.Single(settings.Recent));
            Assert.DoesNotContain("*", Info(w));
            Ui.Click(Button(w, "OpenButton"));   // cancelado
            Assert.Equal(path, w.Session.FilePath);
            Assert.StartsWith("open|", Fake.Pickers[0]);
            Assert.EndsWith("|" + Temp.Path, Fake.Pickers[1]);   // la segunda vez, en la carpeta del fichero
        });
        Assert.Contains("macro.soctask", File.ReadAllText(AppPaths.Current.SettingsFile));
    }

    [Fact]
    public void Abrir_UnRec_LoImportaYAvisa()
    {
        var rec = Temp.File("viejo.rec");
        var bytes = new byte[3 * RecImporter.RecordSize];
        (uint, uint, uint, uint)[] records = [(0x0200, 1, 1, 0), (0x0201, 1, 1, 10), (0x0202, 1, 1, 20)];
        for (var i = 0; i < records.Length; i++)
        {
            var span = bytes.AsSpan(i * 20);
            BinaryPrimitives.WriteUInt32LittleEndian(span, records[i].Item1);
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], records[i].Item2);
            BinaryPrimitives.WriteUInt32LittleEndian(span[8..], records[i].Item3);
            BinaryPrimitives.WriteUInt32LittleEndian(span[12..], records[i].Item4);
            BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0x00010ABC);
        }
        File.WriteAllBytes(rec, bytes);
        Ui.Run(() =>
        {
            var settings = Settings();
            var w = new MainWindow(settings);
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            w.OpenFile(rec);
            Assert.Equal(Loc.Get("RecImported"), message);
            Assert.True(w.Session.Dirty);
            Assert.Null(w.Session.FilePath);
            Assert.Empty(settings.Recent);
        });
    }

    [Fact]
    public void Abrir_UnFicheroQueYaNoEsta_AvisaYLoQuitaDeRecientes()
    {
        var missing = Temp.File("borrado.soctask");
        Ui.Run(() =>
        {
            var settings = Settings();
            settings.Recent.Add(missing);
            var w = new MainWindow(settings);
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            w.OpenFile(missing);
            Assert.Equal(Loc.Format("OpenFailed", "borrado.soctask", Loc.Get(FileErrors.LocKey(new FileNotFoundException()))), message);
            Assert.Empty(settings.Recent);
        });
    }

    [Fact]
    public void Abrir_UnFicheroDañado_Avisa()
    {
        var bad = Temp.File("roto.soctask");
        File.WriteAllBytes(bad, [1, 2, 3, 4, 5]);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            Ui.Answer("OkButton");
            w.OpenFile(bad);
            Assert.False(w.Session.HasRecording);
        });
    }

    [Fact]
    public void Abrir_ConCambios_ElegirGuardar_GuardaPrimero()
    {
        var other = SavedFile("otra.soctask");
        var target = Temp.File("grabada.soctask");
        Fake.SaveAnswers.Enqueue(target);
        Ui.Run(() =>
        {
            var w = Recorded();
            Ui.Answer("OkButton");   // Guardar
            w.OpenFile(other);
            Assert.True(File.Exists(target));
            Assert.Equal(other, w.Session.FilePath);
        });
    }

    [Fact]
    public void Abrir_ConCambios_SiSeCancelaElGuardar_NoAbre()
    {
        var other = SavedFile("otra.soctask");
        Fake.SaveAnswers.Enqueue(null);
        Ui.Run(() =>
        {
            var w = Recorded();
            Ui.Answer("OkButton");
            w.OpenFile(other);
            Assert.Null(w.Session.FilePath);
            Assert.True(w.Session.Dirty);
        });
    }

    [Fact]
    public void Guardar_PideNombreLaPrimeraVez_YLuegoNo()
    {
        var target = Temp.File("nueva.soctask");
        Fake.SaveAnswers.Enqueue(target);
        Ui.Run(() =>
        {
            var settings = Settings();
            var w = Recorded(settings);
            Ui.Click(Button(w, "SaveButton"));
            Assert.Equal(target, w.Session.FilePath);
            Assert.False(w.Session.Dirty);
            Assert.Equal(Loc.Format("StatusSaved", "nueva.soctask"), Status(w));
            Assert.Contains(target, settings.Recent);
            Assert.True(w.Save(saveAs: false));   // ya tiene nombre: no hay dialogo
            Assert.Single(Fake.Pickers);
            Assert.EndsWith("|.soctask", Fake.Pickers[0]);
        });
        Assert.Equal(5, RecordingFile.Load(target).Count);
    }

    [Fact]
    public void Guardar_SinGrabacion_NoHaceNada_YSiFalla_Avisa()
    {
        Fake.SaveAnswers.Enqueue(Temp.Path);   // una carpeta: no se puede escribir como fichero
        Ui.Run(() =>
        {
            Assert.False(new MainWindow(Settings()).Save(saveAs: true));
            var w = Recorded();
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            Assert.False(w.Save(saveAs: true));
            Assert.StartsWith(Loc.Format("SaveFailed", Path.GetFileName(Temp.Path), "")[..10], message);
            Assert.True(w.Session.Dirty);
        });
    }

    [Fact]
    public void Arrastrar_SoloUnFicheroQueSeSabeAbrir()
    {
        var path = SavedFile();
        Assert.Equal(path, MainWindow.DroppedFile(new DataObject(DataFormats.FileDrop, new[] { path })));
        Assert.Null(MainWindow.DroppedFile(new DataObject(DataFormats.FileDrop, new[] { path, path })));
        Assert.Null(MainWindow.DroppedFile(new DataObject(DataFormats.FileDrop, new[] { Temp.File("nota.txt") })));
        Assert.Null(MainWindow.DroppedFile(new DataObject(DataFormats.Text, "hola")));
    }

    [Fact]
    public void Arrastrar_SobreLaVentana_LoAbre()
    {
        var path = SavedFile();
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            var over = DragArgs(data, w);
            over.RoutedEvent = DragDrop.DragOverEvent;
            w.RaiseEvent(over);
            Assert.Equal(DragDropEffects.Copy, over.Effects);
            var drop = DragArgs(data, w);
            drop.RoutedEvent = DragDrop.DropEvent;
            w.RaiseEvent(drop);
            Ui.WaitFor(() => w.Session.FilePath == path);
        });
    }

    private static DragEventArgs DragArgs(IDataObject data, DependencyObject target)
    {
        var ctor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Single();
        return (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(1, 1)]);
    }

    [Fact]
    public void Recuperar_LaCopiaQueDejoLaVersionAnterior()
    {
        RecordingFile.Save(new Recording(Events()), AppPaths.Current.RecoveryFile);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.RecoverIfAny();
            Assert.True(w.Session.HasRecording);
            Assert.True(w.Session.Dirty);
            Assert.Equal(Loc.Get("StatusRecovered"), Status(w));
            w.RecoverIfAny();   // ya no esta
        });
        Assert.False(File.Exists(AppPaths.Current.RecoveryFile));
    }

    [Fact]
    public void Recuperar_UnaCopiaIlegible_SeBorraSinMas()
    {
        File.WriteAllBytes(AppPaths.Current.RecoveryFile, [9, 9, 9]);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.RecoverIfAny();
            Assert.False(w.Session.HasRecording);
        });
        Assert.False(File.Exists(AppPaths.Current.RecoveryFile));
        Assert.Contains("copia de recuperacion ilegible", File.ReadAllText(AppLog.FilePath));
    }

    [Fact]
    public void UnaVersionNueva_PideCerrar_YLoSinGuardarQuedaEnLaCopia()
    {
        Ui.Run(() =>
        {
            var w = Recorded();
            w.Show();
            w.QuitForNewerVersion();
            Assert.True(Ui.IsClosed(w));
        });
        Assert.Equal(5, RecordingFile.Load(AppPaths.Current.RecoveryFile).Count);
    }

    // ------------------------------------------------------------ compilar

    [Fact]
    public void Compilar_SinReproductor_LoDice()
    {
        MainWindow.PlayerStream = () => null;
        Ui.Run(() =>
        {
            var w = Recorded();
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            Ui.Click(Button(w, "CompileButton"));
            Assert.Equal(Loc.Get("CompileNoPlayer"), message);
        });
    }

    private static MemoryStream FakePlayer() => new(ExeTests.FakePlayer());

    [Fact]
    public void Compilar_HaceElExeConLaGrabacion()
    {
        MainWindow.PlayerStream = FakePlayer;
        var exe = Temp.File("macro.exe");
        Fake.SaveAnswers.Enqueue(exe);
        Fake.SaveAnswers.Enqueue(null);
        Ui.Run(() =>
        {
            var w = Recorded();
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            Ui.Click(Button(w, "CompileButton"));
            Assert.Equal(Loc.Format("CompileDone", "macro.exe", Ui.Call(w, "EmergencyText")), message);
            Assert.Equal(Loc.Format("StatusCompiled", "macro.exe"), Status(w));
            Ui.Click(Button(w, "CompileButton"));   // cancelado: nada
        });
        var (recording, options) = PayloadReader.Read(exe);
        Assert.Equal(5, recording.Count);
        Assert.Equal(EmergencyKeys.All, options.Emergency);
    }

    [Fact]
    public void Compilar_EncimaDeLaPropiaAplicacion_NoSeDeja()
    {
        MainWindow.PlayerStream = FakePlayer;
        Fake.ExePath = Temp.File("sOCAutoTask.exe");
        Fake.SaveAnswers.Enqueue(Fake.ExePath);
        Ui.Run(() =>
        {
            var w = Recorded();
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            Ui.Click(Button(w, "CompileButton"));
            Assert.Equal(Loc.Get("CompileOverSelf"), message);
        });
        Assert.False(File.Exists(Fake.ExePath));
    }

    [Fact]
    public void Compilar_SiNoSePuedeEscribir_Avisa()
    {
        MainWindow.PlayerStream = FakePlayer;
        Fake.SaveAnswers.Enqueue(Path.Combine(Temp.Path, "no", "existe", "macro.exe"));
        Ui.Run(() =>
        {
            var w = Recorded();
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            Ui.Click(Button(w, "CompileButton"));
            Assert.StartsWith(Loc.Format("CompileFailed", "macro.exe", "")[..10], message);
        });
    }

    [Fact]
    public void BotonesDeLaGrabacion_SinGrabacion_NoHacenNada()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            Ui.Call(w, "OnCompile", w, new RoutedEventArgs());
            Ui.Call(w, "OnEditor", w, new RoutedEventArgs());
            Assert.Empty(Fake.Pickers);
        });
    }

    // ------------------------------------------------------------ editor y ajustes

    [Fact]
    public void Editor_LoAplicado_CambiaLaGrabacion()
    {
        Ui.Run(() =>
        {
            var w = Recorded();
            w.Show();
            Ui.Expect<EditorWindow>(e =>
            {
                var grid = Ui.Find<DataGrid>(e, "Grid");
                grid.SelectedIndex = 0;
                Ui.Click(Ui.Find<Button>(e, "DeleteButton"));
                Ui.Click(Ui.Find<Button>(e, "ApplyButton"));
            });
            Ui.Click(Button(w, "EditorButton"));
            Assert.Equal(4, w.Session.Recording!.Count);
            Assert.Equal(Loc.Get("StatusEdited"), Status(w));
        });
    }

    [Fact]
    public void Editor_SiSeBorraTodo_NoSeAplica()
    {
        Ui.Run(() =>
        {
            var w = Recorded();
            w.Show();
            Ui.Expect<EditorWindow>(e =>
            {
                Ui.Find<DataGrid>(e, "Grid").SelectAll();
                Ui.Click(Ui.Find<Button>(e, "DeleteButton"));
                Ui.Click(Ui.Find<Button>(e, "ApplyButton"));
            });
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            Ui.Click(Button(w, "EditorButton"));
            Assert.Equal(Loc.Get("EditorEmptyResult"), message);
            Assert.Equal(5, w.Session.Recording!.Count);
        });
    }

    [Fact]
    public void Ajustes_AlGuardar_SeAplicanALaGrabacionYALaVentana()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            var w = Recorded(settings);
            w.Show();
            Ui.Expect<SettingsWindow>(s =>
            {
                Ui.Find<RadioButton>(s, "TimesRadio").IsChecked = true;
                Ui.Find<TextBox>(s, "TimesBox").Text = "3";
                Ui.Find<CheckBox>(s, "TopmostCheck").IsChecked = true;
                Ui.Click(Ui.Find<Button>(s, "SaveButton"));
            });
            Ui.Click(Button(w, "SettingsButton"));
            Assert.Equal(RepeatMode.Times, w.Session.Recording!.Options.Repeat);
            Assert.Equal(3, settings.DefaultPlayback.Times);
            Assert.True(w.Topmost);
            Assert.Contains(Loc.Format("RepeatTimesShort", 3), Info(w));
        });
    }

    [Fact]
    public void Ajustes_AlCancelar_NoCambiaLaGrabacion()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.Show();
            Ui.Expect<SettingsWindow>(s => s.Close());
            w.OpenSettings();
            Assert.False(w.Topmost);
        });
        Assert.True(File.Exists(AppPaths.Current.SettingsFile));
    }

    // ------------------------------------------------------------ menu «Mas»

    [Fact]
    public void Menu_SinGrabacion_GuardarComoDesactivado_YAdministradorSoloSiNoLoEs()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            var menu = w.BuildMoreMenu();
            Assert.False(MenuItem(menu, "SaveAsItem").IsEnabled);
            Assert.True(HasMenuItem(menu, "AdminItem"));
            Assert.False(HasMenuItem(menu, "Recent0"));
            Fake.Elevated = true;
            Assert.False(HasMenuItem(w.BuildMoreMenu(), "AdminItem"));
        });
    }

    [Fact]
    public void Menu_Recientes_LosQueYaNoEstanSeQuitan_YLosDemasSeAbren()
    {
        var path = SavedFile();
        Ui.Run(() =>
        {
            var settings = Settings();
            settings.Recent.Add(Temp.File("borrado.soctask"));
            settings.Recent.Add(path);
            var w = new MainWindow(settings);
            var menu = w.BuildMoreMenu();
            Assert.Equal(path, Assert.Single(settings.Recent));
            var recent = MenuItem(menu, "Recent0");
            Assert.Equal("macro.soctask", recent.Header);
            Assert.Equal(path, recent.ToolTip);
            Ui.Click(recent);
            Assert.Equal(path, w.Session.FilePath);
        });
    }

    [Fact]
    public void Menu_GuardarComo_EImportarRec_UsanSusDialogos()
    {
        var target = Temp.File("como.soctask");
        Fake.SaveAnswers.Enqueue(target);
        Fake.OpenAnswers.Enqueue(null);
        Ui.Run(() =>
        {
            var w = Recorded();
            Ui.Click(MenuItem(w.BuildMoreMenu(), "SaveAsItem"));
            Assert.True(File.Exists(target));
            Ui.Click(MenuItem(w.BuildMoreMenu(), "ImportRecItem"));
            Assert.StartsWith("open|" + Loc.Get("FilterRec"), Fake.Pickers[^1]);
        });
    }

    [Fact]
    public void Menu_CarpetaDeDatos_LaAbreConElExplorador()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            Ui.Click(MenuItem(w.BuildMoreMenu(), "DataFolderItem"));
            var start = Assert.Single(Fake.Started);
            Assert.Equal("explorer.exe", start.FileName);
            Assert.Contains(AppPaths.Current.DataFolder, start.Arguments);
            Fake.StartError = new InvalidOperationException("sin explorador");
            Ui.Click(MenuItem(w.BuildMoreMenu(), "DataFolderItem"));
        });
        Assert.Contains("no se pudo abrir la carpeta de datos", File.ReadAllText(AppLog.FilePath));
    }

    [Fact]
    public void Menu_GuiaNovedadesYAcercaDe_AbrenSusVentanas()
    {
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.Show();
            Ui.Expect<GuideWindow>(g => g.Close());
            Ui.Click(MenuItem(w.BuildMoreMenu(), "GuideItem"));
            Ui.Expect<WhatsNewWindow>(n => n.Close());
            Ui.Click(MenuItem(w.BuildMoreMenu(), "WhatsNewItem"));
            Ui.Expect<AboutWindow>(a => a.Close());
            Ui.Click(MenuItem(w.BuildMoreMenu(), "AboutItem"));
            Ui.Click(MenuItem(w.BuildMoreMenu(), "ExitItem"));
            Assert.True(Ui.IsClosed(w));
        });
    }

    [Fact]
    public void Menu_ReiniciarComoAdministrador_ConElFicheroGuardado()
    {
        var path = SavedFile();
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.OpenFile(path);
            w.Show();
            Ui.Click(MenuItem(w.BuildMoreMenu(), "AdminItem"));
            var start = Assert.Single(Fake.Started);
            Assert.Equal($"--wait-pid {Environment.ProcessId} \"{path}\"", start.Arguments);
            Assert.True(Ui.IsClosed(w));
        });
    }

    [Fact]
    public void ReiniciarComoAdministrador_SiSeDiceQueNo_SigueAbierta()
    {
        Fake.StartError = new Win32Exception(1223);
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.Show();
            w.RestartAsAdmin();
            Assert.False(Ui.IsClosed(w));
        });
    }

    [Fact]
    public void ReiniciarComoAdministrador_SiFalla_Avisa()
    {
        Fake.StartError = new InvalidOperationException("roto");
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.Show();
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            w.RestartAsAdmin();
            Assert.Equal(Loc.Get("RestartFailed"), message);
            Assert.False(Ui.IsClosed(w));
            Sandbox.IsOn = true;
            w.RestartAsAdmin();   // en modo aislado, nunca
            Assert.Single(Fake.Started);
        });
    }

    [Fact]
    public void Idioma_DesdeAcercaDe_SeRecuerda()
    {
        Ui.Run(() =>
        {
            var settings = Settings();
            var w = new MainWindow(settings);
            w.SetLanguage("en");
            Assert.Equal("en", settings.Language);
        });
        Assert.Contains("\"en\"", File.ReadAllText(AppPaths.Current.SettingsFile));
    }

    [Fact]
    public void GuardarAjustes_SiNoSePuede_SoloLoApunta()
    {
        Ui.Run(() =>
        {
            AppPaths.Current = AppPaths.At(Path.Combine(Temp.Path, "settings.json", "dentro"));
            File.WriteAllText(Path.Combine(Temp.Path, "settings.json"), "es un fichero");
            new MainWindow(Settings()).SaveSettings();
        });
    }

    [Fact]
    public void TraerAlFrente_AbreElFicheroQueLePasan()
    {
        var path = SavedFile();
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            w.Show();
            w.WindowState = WindowState.Minimized;
            w.BringToFront(path);
            Assert.Equal(WindowState.Normal, w.WindowState);
            Assert.Equal(path, w.Session.FilePath);
            w.BringToFront(string.Empty);
        });
    }

    // ------------------------------------------------------------ ventana de verdad: atajos y bandeja

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [Fact]
    public void Atajos_SeRegistran_YAlPulsarlosGrabaYReproduce()
    {
        Fake.Recorder.Events = Events();
        Ui.Run(() =>
        {
            var w = new MainWindow(Settings());
            var hwnd = new WindowInteropHelper(w).EnsureHandle();
            Assert.True(w.HotkeysOk);
            SendMessage(hwnd, 0x0312, 1, 0);   // WM_HOTKEY grabar
            Assert.True(Fake.Recorder.Started);
            SendMessage(hwnd, 0x0312, 1, 0);
            Assert.True(w.Session.HasRecording);
            SendMessage(hwnd, 0x0312, 2, 0);   // reproducir
            Ui.WaitFor(() => Idle(w));
            Assert.Equal(5, Fake.Sink.Sent.Count);
            SendMessage(hwnd, 0x0312, 9, 0);   // otro: nada
        });
    }

    [Fact]
    public void Atajos_OcupadosPorOtro_SeAvisa()
    {
        Ui.Run(() =>
        {
            var first = new MainWindow(Settings());
            new WindowInteropHelper(first).EnsureHandle();
            var second = new MainWindow(Settings());
            new WindowInteropHelper(second).EnsureHandle();
            Assert.False(second.HotkeysOk);
            string? message = null;
            Ui.Answer("OkButton", m => message = m);
            var failed = second.RegisterHotkeys(reportFailures: true);
            Assert.Equal(2, failed.Count);
            Assert.Equal(Loc.Format("HotkeyTaken", string.Join(", ", failed)), message);
            Assert.Empty(new MainWindow(Settings()).RegisterHotkeys(reportFailures: true));   // sin ventana: nada que registrar
        });
    }

    [Fact]
    public void Bandeja_MinimizarEsconde_YElIconoLaDevuelve()
    {
        Fake.Recorder.Events = Events();
        Ui.Run(() =>
        {
            var settings = Settings();
            var w = new MainWindow(settings);
            w.Show();
            var hwnd = new WindowInteropHelper(w).Handle;
            SendMessage(hwnd, 0x0112, 0xF020, 0);   // WM_SYSCOMMAND minimizar
            Assert.False(w.IsVisible);
            SendMessage(hwnd, 0x8001, 0, 0x0202);   // clic en el icono
            Assert.True(w.IsVisible);
            w.WindowState = WindowState.Minimized;  // minimizar con el boton: tambien a la bandeja
            Ui.WaitFor(() => !w.IsVisible);
            SendMessage(hwnd, 0x0111, 1, 0);        // menu: Abrir
            Assert.True(w.IsVisible);
            SendMessage(hwnd, 0x0111, 2, 0);        // menu: Grabar
            Assert.True(Fake.Recorder.Started);
            SendMessage(hwnd, 0x0111, 2, 0);        // menu: Parar
            SendMessage(hwnd, 0x0111, 3, 0);        // menu: Reproducir
            Ui.WaitFor(() => Idle(w));
            settings.TrayOnMinimize = false;
            w.ApplyWindowSettings();
            SendMessage(hwnd, 0x0112, 0xF020, 0);   // ya no va a la bandeja
            SendMessage(hwnd, 0x0111, 77, 0);       // orden desconocida
            w.WindowState = WindowState.Normal;
            w.Hide();
            w.BringToFront(string.Empty);           // escondida: vuelve de la bandeja
            Assert.True(w.IsVisible);
            Ui.Answer("DiscardButton");
            SendMessage(hwnd, 0x0111, 4, 0);        // menu: Salir
            Assert.True(Ui.IsClosed(w));
        });
    }
}
