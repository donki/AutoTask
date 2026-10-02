using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using SocAutoTask.AppServices;
using SocAutoTask.Compile;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Editing;
using SocAutoTask.Format;
using SocAutoTask.Localization;
using SocAutoTask.Model;
using SocAutoTask.Native;
using SocAutoTask.Playback;

namespace SocAutoTask.Desktop;

/// <summary>
/// La barra principal. Solo pegamento: la logica (grabar, reproducir, ficheros, editor) esta en
/// AutoTask.Core. Estados: parado, grabando o reproduciendo; uno cada vez.
/// </summary>
public partial class MainWindow : Window
{
    private enum UiState { Idle, Recording, Playing }

    /// <summary>Recurso incrustado con el reproductor de los exe compilados (ARQUITECTURA §6).</summary>
    public const string PlayerResource = "AutoTask.Player.exe";

    private readonly AppSettings _settings;
    private readonly MacroSession _session = new();
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private UiState _state = UiState.Idle;
    private IRecorder? _recorder;
    private Stopwatch? _recordWatch;
    private PlaybackSession? _playback;
    private IEmergencyWatcher? _emergency;
    private bool _elevatedWarning;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkeys;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        if (Sandbox.IsOn)
            Title += " [SOC_SANDBOX]";

        _session.Changed += RefreshInfo;
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
        _ticker.Tick += (_, _) => Tick();
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = DroppedFile(e.Data) is not null && _state == UiState.Idle ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        LocationChanged += (_, _) => UpdateIgnoredArea();
        SizeChanged += (_, _) => UpdateIgnoredArea();

        ApplyWindowSettings();
        RestorePosition();
        RefreshTooltips();
        RefreshInfo();
        SetIdleStatus("StatusReady");
    }

    public MacroSession Session => _session;

    private void OnLanguageChanged()
    {
        RefreshStateTexts();
        RefreshTooltips();
        RefreshInfo();
        SetIdleStatus(_idleStatusKey, _idleStatusArgs);
    }

    // =====================================================================
    //  Arranque y cierre
    // =====================================================================

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ThemeManager.ApplyToWindow(this);
        if (Sandbox.IsOn)
            return;   // ni atajos globales ni bandeja en modo aislado (§8.4)
        _hotkeys = new HotkeyManager(this);
        _hotkeys.Pressed += id =>
        {
            if (id == HotkeyManager.RecordId) ToggleRecording();
            else if (id == HotkeyManager.PlayId) TogglePlayback();
        };
        RegisterHotkeys(reportFailures: false);
        _tray = new TrayIcon(this, Loc.Get, ToggleRecording, TogglePlayback, Close)
        {
            MinimizeToTray = _settings.TrayOnMinimize,
        };
    }

    /// <summary>Registra los dos atajos. Devuelve los que no se pudieron (los tiene otro programa).</summary>
    public List<string> RegisterHotkeys(bool reportFailures)
    {
        var failed = new List<string>();
        if (_hotkeys is null)
            return failed;
        if (!_hotkeys.Register(HotkeyManager.RecordId, _settings.RecordKey))
            failed.Add(_settings.RecordKey.Display());
        if (!_hotkeys.Register(HotkeyManager.PlayId, _settings.PlayKey))
            failed.Add(_settings.PlayKey.Display());
        if (failed.Count > 0)
        {
            AppLog.Write($"atajos sin registrar: {string.Join(", ", failed)}");
            if (reportFailures)
                PromptWindow.Alert(this, Loc.Get("HotkeyTitle"), Loc.Format("HotkeyTaken", string.Join(", ", failed)));
        }
        RefreshTooltips();
        return failed;
    }

    /// <summary>¿Estan los atajos registrados? (para la guia)</summary>
    public bool HotkeysOk => Sandbox.IsOn || (_hotkeys?.IsRegistered(HotkeyManager.RecordId) == true && _hotkeys.IsRegistered(HotkeyManager.PlayId));

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_state == UiState.Recording)
            StopRecording();
        if (_state == UiState.Playing)
        {
            // Parar y esperar a que suelte las teclas antes de salir (RF-14).
            _playback?.Control.Stop(StopReason.Closing);
            _playbackThread?.Join(2000);
        }
        if (!_quitRequested && !ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        SavePosition();
        SaveSettings();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _emergency?.Dispose();
    }

    private bool _quitRequested;

    /// <summary>
    /// Una version mas nueva pide que esta se cierre (§8.3): lo que no este guardado se deja en la
    /// copia de recuperacion, que la nueva abre al arrancar.
    /// </summary>
    public void QuitForNewerVersion()
    {
        _quitRequested = true;
        try
        {
            if (_session.Dirty && _session.Recording is { IsEmpty: false } recording)
                RecordingFile.Save(recording, AppPaths.Current.RecoveryFile);
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo dejar la copia de recuperacion: {ex}");
        }
        Close();
    }

    // =====================================================================
    //  Ajustes de la ventana
    // =====================================================================

    public void ApplyWindowSettings()
    {
        Topmost = _settings.AlwaysOnTop;
        Application.Current.Resources["LabelVisibility"] = _settings.ShowLabels ? Visibility.Visible : Visibility.Collapsed;
        if (_tray is not null)
            _tray.MinimizeToTray = _settings.TrayOnMinimize;
    }

    private void RestorePosition()
    {
        if (_settings.WindowLeft is not { } left || _settings.WindowTop is not { } top)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        // Solo si cae dentro de la pantalla de ahora (un monitor que ya no esta la dejaria fuera).
        if (left >= SystemParameters.VirtualScreenLeft && top >= SystemParameters.VirtualScreenTop &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void SavePosition()
    {
        if (WindowState != WindowState.Normal || !IsVisible)
            return;
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
    }

    public void SaveSettings()
    {
        try
        {
            _settings.Save(AppPaths.Current.SettingsFile);
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudieron guardar los ajustes: {ex}");
        }
    }

    // =====================================================================
    //  Estado y textos
    // =====================================================================

    private string _idleStatusKey = "StatusReady";
    private object[] _idleStatusArgs = [];

    private void SetIdleStatus(string key, params object[] args)
    {
        _idleStatusKey = key;
        _idleStatusArgs = args;
        if (_state == UiState.Idle)
        {
            StatusText.Text = args.Length > 0 ? Loc.Format(key, args) : Loc.Get(key);
            StateDot.Fill = (System.Windows.Media.Brush)FindResource("Separator");
        }
    }

    private void RefreshTooltips()
    {
        RecordButton.ToolTip = Loc.Format(_state == UiState.Recording ? "StopRecordTip" : "RecordTip", _settings.RecordKey.Display());
        PlayButton.ToolTip = Loc.Format(_state == UiState.Playing ? "StopPlayTip" : "PlayTip", _settings.PlayKey.Display(), EmergencyText());
    }

    private string EmergencyText()
    {
        var keys = new List<string>();
        if (_settings.Emergency.HasFlag(Input.EmergencyKeys.Pause)) keys.Add(Loc.Get("Key_Pause"));
        if (_settings.Emergency.HasFlag(Input.EmergencyKeys.ScrollLock)) keys.Add(Loc.Get("Key_ScrollLock"));
        if (_settings.Emergency.HasFlag(Input.EmergencyKeys.EscapeHold)) keys.Add(Loc.Format("EscHeld", _settings.EscapeHoldMs / 1000.0));
        return keys.Count == 0 ? Loc.Get("EmergencyNone") : string.Join(" · ", keys);
    }

    private void RefreshInfo()
    {
        var recording = _session.Recording;
        if (recording is null || recording.IsEmpty)
        {
            InfoText.Text = Loc.Get("InfoEmpty");
        }
        else
        {
            var o = recording.Options;
            var repeat = o.Repeat switch
            {
                RepeatMode.Once => Loc.Get("RepeatOnceShort"),
                RepeatMode.Times => Loc.Format("RepeatTimesShort", o.Times),
                _ => Loc.Get("RepeatContinuousShort"),
            };
            InfoText.Text = Loc.Format("InfoLine", _session.DisplayName + (_session.Dirty ? " *" : string.Empty),
                recording.Count, EventDescriber.FormatMs(recording.TotalMs), EventDescriber.Speed(o.Speed), repeat);
        }
        var idle = _state == UiState.Idle;
        var has = _session.HasRecording;
        SaveButton.IsEnabled = idle && has;
        CompileButton.IsEnabled = idle && has;
        EditorButton.IsEnabled = idle && has;
        PlayButton.IsEnabled = _state != UiState.Recording && has;
        RecordButton.IsEnabled = _state != UiState.Playing;
        OpenButton.IsEnabled = idle;
        SettingsButton.IsEnabled = idle;
    }

    private void SetState(UiState state)
    {
        _state = state;
        RecordButton.Style = (Style)FindResource(state == UiState.Recording ? "ToolButtonActive" : "ToolButton");
        RecordButton.Tag = state == UiState.Recording ? "stop" : "record";
        PlayButton.Style = (Style)FindResource(state == UiState.Playing ? "ToolButtonActive" : "ToolButton");
        PlayButton.Tag = state == UiState.Playing ? "stop" : "play";
        RefreshStateTexts();
        if (state == UiState.Idle)
        {
            _ticker.Stop();
            SetIdleStatus(_idleStatusKey, _idleStatusArgs);
        }
        else
        {
            StateDot.Fill = (System.Windows.Media.Brush)FindResource("Danger");
            _ticker.Start();
        }
        RefreshTooltips();
        RefreshInfo();
    }

    private void RefreshStateTexts()
    {
        RecordButton.Content = Loc.Get(_state == UiState.Recording ? "StopShort" : "Record");
        PlayButton.Content = Loc.Get(_state == UiState.Playing ? "StopShort" : "Play");
    }

    private void Tick()
    {
        if (_state == UiState.Recording && _recorder is not null && _recordWatch is not null)
        {
            StatusText.Text = Loc.Format("StatusRecording", EventDescriber.Clock(_recordWatch.Elapsed.TotalMilliseconds), _recorder.Count);
            CheckElevatedForeground();
        }
        else if (_state == UiState.Playing)
        {
            CheckElevatedForeground();
        }
    }

    private void CheckElevatedForeground()
    {
        if (_elevatedWarning || !Platform.Current.ForegroundIsElevatedAndWeAreNot())
            return;
        _elevatedWarning = true;
        AppLog.Write("ventana elevada en primer plano sin AutoTask elevado");
    }

    // =====================================================================
    //  Grabar (RF-01..10)
    // =====================================================================

    private void OnRecord(object sender, RoutedEventArgs e) => ToggleRecording();

    public void ToggleRecording()
    {
        if (_state == UiState.Recording)
            StopRecording();
        else if (_state == UiState.Idle)
            StartRecording();
    }

    private void StartRecording()
    {
        if (Sandbox.IsOn)
        {
            SetIdleStatus("SandboxNoRecord");
            return;
        }
        if (!ConfirmDiscard())
            return;
        _recorder = Platform.Current.CreateRecorder(new RecorderOptions { Keyboard = _settings.RecordKeyboard, MouseMoves = _settings.RecordMouseMoves });
        try
        {
            _recorder.Start();
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo empezar a grabar: {ex}");
            _recorder = null;
            PromptWindow.Alert(this, Loc.Get("RecordFailedTitle"), Loc.Get("RecordFailed"));
            return;
        }
        UpdateIgnoredArea();
        _recordWatch = Stopwatch.StartNew();
        _elevatedWarning = false;
        SetState(UiState.Recording);
        Tick();
    }

    private void StopRecording()
    {
        if (_recorder is null)
            return;
        var recording = _recorder.StopAndCollect(_settings.DefaultPlayback);
        _recorder.Dispose();
        _recorder = null;
        var elevated = _elevatedWarning;
        if (recording.IsEmpty)
        {
            _idleStatusKey = "StatusNothingRecorded";
            _idleStatusArgs = [];
        }
        else
        {
            _session.SetRecorded(recording);
            _idleStatusKey = "StatusRecorded";
            _idleStatusArgs = [recording.Count, EventDescriber.FormatMs(recording.TotalMs)];
        }
        SetState(UiState.Idle);
        if (elevated)
            OfferElevation("ElevatedWhileRecording");
    }

    /// <summary>Los clics sobre la propia ventana no se graban (RF-05): su rectangulo en pixeles fisicos.</summary>
    private void UpdateIgnoredArea()
    {
        if (_recorder is null)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r))
            _recorder.SetIgnoredArea(new ScreenRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
    }

    // =====================================================================
    //  Reproducir (RF-11..18)
    // =====================================================================

    private Thread? _playbackThread;

    private void OnPlay(object sender, RoutedEventArgs e) => TogglePlayback();

    public void TogglePlayback()
    {
        if (_state == UiState.Playing)
            _playback?.Control.Stop(StopReason.User);
        else if (_state == UiState.Idle)
            StartPlayback();
    }

    private void StartPlayback()
    {
        if (_session.Recording is not { IsEmpty: false } recording)
            return;
        if (Sandbox.IsOn)
        {
            // Nunca se mueve el raton de quien trabaja desde una prueba de interfaz (§8.4).
            SetIdleStatus("SandboxNoPlay");
            return;
        }

        var screen = Screens.Virtual();
        var screenChanged = !recording.Screen.IsEmpty && recording.Screen != screen;

        var platform = Platform.Current;
        var sink = platform.CreateSink();
        _playback = new PlaybackSession(recording.Events, recording.Options, sink, platform.CreateClock(), platform.Keyboard, _settings.CountdownSeconds);
        _playback.Status += s => Dispatcher.BeginInvoke(() => ShowPlaybackStatus(s, screenChanged));
        try
        {
            _emergency = platform.CreateEmergency(_settings.Emergency, _settings.EscapeHoldMs, () => _playback?.Control.Stop(StopReason.Emergency));
            if (_settings.Emergency != Input.EmergencyKeys.None)
                _emergency.Start();
        }
        catch (Exception ex)
        {
            // Sin parada de emergencia no se reproduce: es la red de seguridad (RF-13).
            AppLog.Write($"no se pudo poner la parada de emergencia: {ex}");
            _emergency = null;
            PromptWindow.Alert(this, Loc.Get("PlayFailedTitle"), Loc.Get("EmergencyFailed"));
            return;
        }
        _elevatedWarning = false;
        SetState(UiState.Playing);
        var session = _playback;
        _playbackThread = session.Start(reason => Dispatcher.BeginInvoke(() => PlaybackFinished(session, sink, reason)));
    }

    private void ShowPlaybackStatus(PlaybackStatus s, bool screenChanged)
    {
        if (_state != UiState.Playing)
            return;
        var text = s.Phase switch
        {
            PlaybackPhase.WaitingForKeys => Loc.Get("StatusReleaseKeys"),
            PlaybackPhase.Countdown => Loc.Format("StatusCountdown", s.CountdownSeconds),
            PlaybackPhase.PauseBetweenLoops => Loc.Format("StatusPause", LoopText(s)),
            PlaybackPhase.Playing => s.RemainingTotalMs is { } total
                ? Loc.Format("StatusPlaying", LoopText(s), EventDescriber.Clock(s.RemainingLoopMs), EventDescriber.Clock(total))
                : Loc.Format("StatusPlayingContinuous", LoopText(s), EventDescriber.Clock(s.RemainingLoopMs)),
            _ => StatusText.Text,
        };
        if (screenChanged && s.Phase is PlaybackPhase.Playing or PlaybackPhase.Countdown)
            text += " · " + Loc.Get("ScreenChanged");
        StatusText.Text = text;
    }

    private static string LoopText(PlaybackStatus s) =>
        s.Loops is { } loops ? Loc.Format("LoopOf", s.Loop, loops) : Loc.Format("LoopContinuous", s.Loop);

    private void PlaybackFinished(PlaybackSession session, ICountingSink sink, StopReason reason)
    {
        if (!ReferenceEquals(session, _playback))
            return;
        _emergency?.Dispose();
        _emergency = null;
        _playback = null;
        _playbackThread = null;
        _idleStatusKey = reason switch
        {
            StopReason.None => "StatusFinished",
            StopReason.Emergency => "StatusEmergency",
            _ => "StatusStopped",
        };
        _idleStatusArgs = [];
        SetState(UiState.Idle);
        session.Control.Dispose();
        if (sink.Rejected > 0)
            AppLog.Write($"SendInput rechazo {sink.Rejected} eventos");
        if (_elevatedWarning || sink.Rejected > 0)
            OfferElevation("ElevatedWhilePlaying");
    }

    /// <summary>RF-18: avisa de que la ventana era de administrador y ofrece reiniciar elevado.</summary>
    private void OfferElevation(string key)
    {
        _elevatedWarning = false;
        if (Platform.Current.IsElevated)
            return;
        if (PromptWindow.Confirm(this, Loc.Get("ElevatedTitle"), Loc.Get(key)))
            RestartAsAdmin();
    }

    // =====================================================================
    //  Ficheros (RF-22..28)
    // =====================================================================

    private void OnOpen(object sender, RoutedEventArgs e) => ChooseAndOpen(rec: false);

    private void ChooseAndOpen(bool rec)
    {
        if (_state != UiState.Idle)
            return;
        var file = Platform.Current.PickOpenFile(this, Loc.Get(rec ? "FilterRec" : "FilterOpen"), InitialFolder());
        if (file is not null)
            OpenFile(file);
    }

    /// <summary>Abre una grabacion (.soctask, .rec o un exe compilado). Errores explicados (RF-23).</summary>
    public void OpenFile(string path)
    {
        if (_state != UiState.Idle || !ConfirmDiscard())
            return;
        try
        {
            var (recording, kind) = RecordingLoader.Open(path);
            _session.SetOpened(recording, path, kind);
            if (kind == LoadedKind.SocTask)
            {
                _settings.AddRecent(path);
                SaveSettings();
            }
            SetIdleStatus("StatusOpened", Path.GetFileName(path));
            if (kind == LoadedKind.Rec)
                PromptWindow.Alert(this, Loc.Get("RecImportedTitle"), Loc.Get("RecImported"));
        }
        catch (Exception ex) when (ex is RecordingFormatException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Write($"no se pudo abrir {path}: {ex.Message}");
            PromptWindow.Alert(this, Loc.Get("OpenFailedTitle"), Loc.Format("OpenFailed", Path.GetFileName(path), Loc.Get(FileErrors.LocKey(ex))));
            if (ex is FileNotFoundException or DirectoryNotFoundException && _settings.PruneRecent(File.Exists))
                SaveSettings();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save(saveAs: false);

    /// <summary>Guarda. Devuelve falso si se cancela o falla.</summary>
    public bool Save(bool saveAs)
    {
        if (_session.Recording is not { IsEmpty: false } recording)
            return false;
        var path = saveAs ? null : _session.FilePath;
        if (path is null)
        {
            path = Platform.Current.PickSaveFile(this, Loc.Get("FilterSave"), _session.SuggestedName(RecordingFile.Extension), InitialFolder(), RecordingFile.Extension);
            if (path is null)
                return false;
        }
        try
        {
            RecordingFile.Save(recording, path);
            _session.MarkSaved(path);
            _settings.AddRecent(path);
            SaveSettings();
            SetIdleStatus("StatusSaved", Path.GetFileName(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"no se pudo guardar {path}: {ex}");
            PromptWindow.Alert(this, Loc.Get("SaveFailedTitle"), Loc.Format("SaveFailed", Path.GetFileName(path), Loc.Get(FileErrors.LocKey(ex))));
            return false;
        }
    }

    /// <summary>RF-08, RF-50: si hay cambios sin guardar, pregunta. Falso si se cancela.</summary>
    private bool ConfirmDiscard()
    {
        if (!_session.Dirty || !_session.HasRecording)
            return true;
        return PromptWindow.AskUnsaved(this, Loc.Format("UnsavedText", _session.DisplayName)) switch
        {
            UnsavedChoice.Save => Save(saveAs: false),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    private string InitialFolder() => _session.SuggestedFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>El reproductor incrustado (las pruebas pueden poner otro, o ninguno).</summary>
    internal static Func<Stream?> PlayerStream { get; set; } =
        () => Assembly.GetExecutingAssembly().GetManifestResourceStream(PlayerResource);

    internal static string? DroppedFile(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && RecordingLoader.IsSupported(files[0]) ? files[0] : null;

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedFile(e.Data) is { } path)
            Dispatcher.BeginInvoke(() => OpenFile(path));
    }

    // =====================================================================
    //  Compilar (RF-29..33)
    // =====================================================================

    private void OnCompile(object sender, RoutedEventArgs e)
    {
        if (_session.Recording is not { IsEmpty: false } recording)
            return;
        using var player = PlayerStream();
        if (player is null)
        {
            PromptWindow.Alert(this, Loc.Get("CompileTitle"), Loc.Get("CompileNoPlayer"));
            return;
        }
        var path = Platform.Current.PickSaveFile(this, Loc.Get("FilterExe"), _session.SuggestedName(".exe"), InitialFolder(), ".exe");
        if (path is null)
            return;
        if (string.Equals(Path.GetFullPath(path), Platform.Current.ExePath, StringComparison.OrdinalIgnoreCase))
        {
            PromptWindow.Alert(this, Loc.Get("CompileTitle"), Loc.Get("CompileOverSelf"));
            return;
        }
        try
        {
            ExeBuilder.Build(player, recording, new ExeOptions
            {
                Emergency = _settings.Emergency,
                EscapeHoldMs = _settings.EscapeHoldMs,
                CountdownSeconds = _settings.CountdownSeconds,
            }, path);
            SetIdleStatus("StatusCompiled", Path.GetFileName(path));
            PromptWindow.Alert(this, Loc.Get("CompileTitle"), Loc.Format("CompileDone", Path.GetFileName(path), EmergencyText()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or RecordingFormatException)
        {
            AppLog.Write($"no se pudo compilar {path}: {ex}");
            PromptWindow.Alert(this, Loc.Get("CompileTitle"), Loc.Format("CompileFailed", Path.GetFileName(path), Loc.Get(FileErrors.LocKey(ex))));
        }
    }

    // =====================================================================
    //  Editor, ajustes y menu
    // =====================================================================

    private void OnEditor(object sender, RoutedEventArgs e)
    {
        if (_session.Recording is not { IsEmpty: false } recording)
            return;
        var editor = new EditorWindow(recording.Events) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is { } events)
        {
            if (events.Count == 0)
            {
                PromptWindow.Alert(this, Loc.Get("EditorTitle"), Loc.Get("EditorEmptyResult"));
                return;
            }
            _session.ReplaceEvents(events);
            SetIdleStatus("StatusEdited");
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        var window = new SettingsWindow(_settings, _session.Recording?.Options ?? _settings.DefaultPlayback) { Owner = this };
        // Mientras se eligen atajos, los globales fuera: si no, Windows se tragaria la combinacion.
        _hotkeys?.Unregister(HotkeyManager.RecordId);
        _hotkeys?.Unregister(HotkeyManager.PlayId);
        if (window.ShowDialog() != true)
        {
            RegisterHotkeys(reportFailures: false);
            SaveSettings();   // idioma y tema se aplican al momento aunque se cancele
            return;
        }
        if (_session.Recording is not null)
            _session.SetOptions(window.Playback);
        _settings.DefaultPlayback = window.Playback;
        ApplyWindowSettings();
        RegisterHotkeys(reportFailures: true);
        SaveSettings();
        RefreshInfo();
    }

    private void OnMore(object sender, RoutedEventArgs e) => BuildMoreMenu().IsOpen = true;

    /// <summary>El menu de «Mas»: recientes, guardar como, importar, carpeta, guia, novedades, administrador, acerca de y salir.</summary>
    internal ContextMenu BuildMoreMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("ThemedMenu"), PlacementTarget = MoreButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        MenuItem Item(string id, string icon, string text, Action action, bool enabled = true)
        {
            var item = new MenuItem { Style = (Style)FindResource("ThemedMenuItem"), Tag = icon, Header = text, IsEnabled = enabled };
            System.Windows.Automation.AutomationProperties.SetAutomationId(item, id);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        if (_settings.PruneRecent(File.Exists))
            SaveSettings();
        var idle = _state == UiState.Idle;
        for (var i = 0; i < _settings.Recent.Count; i++)
        {
            var path = _settings.Recent[i];
            Item($"Recent{i}", "recent", Path.GetFileName(path), () => OpenFile(path), idle).ToolTip = path;
        }
        if (_settings.Recent.Count > 0)
            menu.Items.Add(new Separator { Background = (System.Windows.Media.Brush)FindResource("Separator") });
        Item("SaveAsItem", "save", Loc.Get("SaveAs"), () => Save(saveAs: true), idle && _session.HasRecording);
        Item("ImportRecItem", "import", Loc.Get("ImportRec"), () => ChooseAndOpen(rec: true), idle);
        Item("DataFolderItem", "folder", Loc.Get("OpenDataFolder"), OpenDataFolder);
        menu.Items.Add(new Separator { Background = (System.Windows.Media.Brush)FindResource("Separator") });
        Item("GuideItem", "guide", Loc.Get("Guide"), () => ShowGuide());
        Item("WhatsNewItem", "news", Loc.Get("WhatsNew"), () => new WhatsNewWindow { Owner = this }.ShowDialog());
        if (!Platform.Current.IsElevated)
            Item("AdminItem", "shield", Loc.Get("RestartAsAdmin"), RestartAsAdmin, idle);
        Item("AboutItem", "info", Loc.Get("About"), () => new AboutWindow { Owner = this }.ShowDialog());
        Item("ExitItem", "exit", Loc.Get("Exit"), Close);
        return menu;
    }

    /// <summary>Idioma elegido en «Acerca de»: se recuerda.</summary>
    public void SetLanguage(string language)
    {
        _settings.Language = language;
        SaveSettings();
    }

    public void ShowGuide() => new GuideWindow(this, _settings) { Owner = this }.ShowDialog();

    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Current.DataFolder);
            Platform.Current.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Current.DataFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo abrir la carpeta de datos: {ex}");
        }
    }

    /// <summary>
    /// Reinicia elevado (RF-18). La copia nueva espera a que esta salga (--wait-pid) para no
    /// pasarle el turno por la instancia unica. Si se cancela el aviso de Windows, no pasa nada.
    /// </summary>
    public void RestartAsAdmin()
    {
        if (_state != UiState.Idle || Sandbox.IsOn || !ConfirmDiscard())
            return;
        var args = $"--wait-pid {Environment.ProcessId}";
        if (_session.FilePath is { } file && !_session.Dirty)
            args += $" \"{file}\"";
        try
        {
            Platform.Current.Start(new ProcessStartInfo(Platform.Current.ExePath!, args) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return;   // la persona dijo que no en el aviso de Windows
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo reiniciar como administrador: {ex}");
            PromptWindow.Alert(this, Loc.Get("ElevatedTitle"), Loc.Get("RestartFailed"));
            return;
        }
        _quitRequested = true;   // lo sin guardar ya se pregunto
        ((App)Application.Current).ReleaseInstance();
        Close();
    }

    /// <summary>Otra copia pidio que esta se enseñe (con un fichero, quiza).</summary>
    public void BringToFront(string file)
    {
        if (!IsVisible)
            _tray?.Restore();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        if (file.Length > 0)
            OpenFile(file);
    }

    /// <summary>Al arrancar: si la version anterior dejo una grabacion sin guardar, se recupera.</summary>
    public void RecoverIfAny()
    {
        var path = AppPaths.Current.RecoveryFile;
        if (!File.Exists(path))
            return;
        try
        {
            var recording = RecordingFile.Load(path);
            if (!recording.IsEmpty)
            {
                _session.SetRecorded(recording);
                SetIdleStatus("StatusRecovered");
            }
        }
        catch (Exception ex) when (ex is RecordingFormatException or IOException)
        {
            AppLog.Write($"copia de recuperacion ilegible: {ex.Message}");
        }
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
}
