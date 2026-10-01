using System.Windows;
using System.Windows.Controls;
using SocAutoTask.AppServices;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Editing;
using SocAutoTask.Input;
using SocAutoTask.Localization;
using SocAutoTask.Model;

namespace SocAutoTask.Desktop;

/// <summary>
/// Ajustes. Idioma y tema se aplican al momento (en caliente); lo demas, al pulsar Guardar, y
/// solo si todo es valido (§6.8: si algo no vale, se dice y no se cierra).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly bool _canAssociate;

    public SettingsWindow(AppSettings settings, PlaybackOptions playback)
    {
        _settings = settings;
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        Playback = playback;

        // Velocidad: las de siempre y «personalizada» (RF-19).
        foreach (var s in PlaybackOptions.PresetSpeeds)
            SpeedCombo.Items.Add(new ComboBoxItem { Content = EventDescriber.Speed(s), Tag = s });
        SpeedCombo.Items.Add(new ComboBoxItem { Content = Loc.Get("CustomSpeed"), Tag = null });
        var presetIndex = Array.IndexOf(PlaybackOptions.PresetSpeeds, playback.Speed);
        SpeedCombo.SelectedIndex = presetIndex >= 0 ? presetIndex : PlaybackOptions.PresetSpeeds.Length;
        CustomSpeedBox.Text = InputParsing.Show(playback.Speed, Loc.Culture);
        CustomSpeedBox.IsEnabled = presetIndex < 0;

        OnceRadio.IsChecked = playback.Repeat == RepeatMode.Once;
        TimesRadio.IsChecked = playback.Repeat == RepeatMode.Times;
        ContinuousRadio.IsChecked = playback.Repeat == RepeatMode.Continuous;
        TimesBox.Text = playback.Times.ToString(Loc.Culture);
        PauseBox.Text = InputParsing.Show(playback.PauseBetweenMs / 1000.0, Loc.Culture);
        CountdownBox.Text = settings.CountdownSeconds.ToString(Loc.Culture);

        RecordHotkeyBox.Hotkey = settings.RecordKey;
        PlayHotkeyBox.Hotkey = settings.PlayKey;
        PauseKeyCheck.IsChecked = settings.Emergency.HasFlag(EmergencyKeys.Pause);
        ScrollLockCheck.IsChecked = settings.Emergency.HasFlag(EmergencyKeys.ScrollLock);
        EscapeCheck.IsChecked = settings.Emergency.HasFlag(EmergencyKeys.EscapeHold);
        EscapeHoldBox.Text = InputParsing.Show(settings.EscapeHoldMs / 1000.0, Loc.Culture);

        KeyboardCheck.IsChecked = settings.RecordKeyboard;
        MovesCheck.IsChecked = settings.RecordMouseMoves;
        TopmostCheck.IsChecked = settings.AlwaysOnTop;
        LabelsCheck.IsChecked = settings.ShowLabels;
        TrayCheck.IsChecked = settings.TrayOnMinimize;

        foreach (var mode in Enum.GetValues<AppTheme>())
            ThemeCombo.Items.Add(new ComboBoxItem { Content = Loc.Get("Theme" + mode), Tag = mode });
        ThemeCombo.SelectedIndex = (int)settings.Theme;

        // La asociacion: en el MSIX la pone el paquete; en modo aislado no se toca el registro.
        _canAssociate = !AppInfo.IsPackaged && !Sandbox.IsOn && Environment.ProcessPath is not null;
        AssociateCheck.IsEnabled = _canAssociate;
        AssociateCheck.IsChecked = AppInfo.IsPackaged || (_canAssociate && FileAssociation.IsApplied(Environment.ProcessPath!));
        if (AppInfo.IsPackaged)
            AssociateHint.Text = Loc.Get("AssociatePackaged");
        DataFolderText.Text = Loc.Format(AppPaths.Current.Portable ? "DataFolderPortable" : "DataFolder", AppPaths.Current.DataFolder);

        PaintLanguageButtons();
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>Velocidad y repeticiones elegidas (validas tras Guardar).</summary>
    public PlaybackOptions Playback { get; private set; }

    private void OnLanguageChanged()
    {
        PaintLanguageButtons();
        ((ComboBoxItem)SpeedCombo.Items[^1]).Content = Loc.Get("CustomSpeed");
        foreach (ComboBoxItem item in ThemeCombo.Items)
            item.Content = Loc.Get("Theme" + item.Tag);
        DataFolderText.Text = Loc.Format(AppPaths.Current.Portable ? "DataFolderPortable" : "DataFolder", AppPaths.Current.DataFolder);
        if (AppInfo.IsPackaged)
            AssociateHint.Text = Loc.Get("AssociatePackaged");
    }

    private void OnSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomSpeedBox is null)
            return;
        var custom = (SpeedCombo.SelectedItem as ComboBoxItem)?.Tag is null;
        CustomSpeedBox.IsEnabled = custom;
        if (custom)
            CustomSpeedBox.Focus();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is ComboBoxItem { Tag: AppTheme mode } && IsLoaded)
        {
            _settings.Theme = mode;
            ThemeManager.Apply(mode);
        }
    }

    private void OnSpanish(object sender, RoutedEventArgs e) => UseLanguage("es");

    private void OnEnglish(object sender, RoutedEventArgs e) => UseLanguage("en");

    private void UseLanguage(string language)
    {
        _settings.Language = language;
        Loc.Use(language);
    }

    private void PaintLanguageButtons()
    {
        var spanish = Loc.Language == "es";
        SpanishButton.Style = (Style)FindResource(spanish ? "FilledButton" : "OutlineButton");
        EnglishButton.Style = (Style)FindResource(spanish ? "OutlineButton" : "FilledButton");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var repeat = TimesRadio.IsChecked == true ? RepeatMode.Times : ContinuousRadio.IsChecked == true ? RepeatMode.Continuous : RepeatMode.Once;
        var preset = (SpeedCombo.SelectedItem as ComboBoxItem)?.Tag as double?;
        if (InputParsing.TryPlayback(preset, CustomSpeedBox.Text, repeat, TimesBox.Text, PauseBox.Text, out var playback) is { } error)
        {
            Fail(error);
            return;
        }
        if (!InputParsing.TrySeconds(CountdownBox.Text, 0, 10, out var countdown))
        {
            Fail("InvalidCountdown");
            return;
        }
        var escape = EscapeCheck.IsChecked == true;
        var holdMs = _settings.EscapeHoldMs;
        if (escape && !InputParsing.TrySecondsToMs(EscapeHoldBox.Text, 0.2, 10, out holdMs))
        {
            Fail("InvalidEscapeHold");
            return;
        }
        var record = RecordHotkeyBox.Hotkey;
        var play = PlayHotkeyBox.Hotkey;
        if (!record.IsValid || !play.IsValid)
        {
            Fail("HotkeyNeedsModifier");
            return;
        }
        if (record == play)
        {
            Fail("HotkeysEqual");
            return;
        }
        var emergency = (PauseKeyCheck.IsChecked == true ? EmergencyKeys.Pause : 0)
            | (ScrollLockCheck.IsChecked == true ? EmergencyKeys.ScrollLock : 0)
            | (escape ? EmergencyKeys.EscapeHold : 0);
        if (emergency == EmergencyKeys.None && !PromptWindow.Confirm(this, Loc.Get("EmergencyTitle"), Loc.Get("EmergencyNoneConfirm")))
            return;

        Playback = playback;
        _settings.CountdownSeconds = countdown;
        _settings.EscapeHoldMs = holdMs;
        _settings.Emergency = emergency;
        _settings.RecordHotkey = record.ToString();
        _settings.PlayHotkey = play.ToString();
        _settings.RecordKeyboard = KeyboardCheck.IsChecked == true;
        _settings.RecordMouseMoves = MovesCheck.IsChecked == true;
        _settings.AlwaysOnTop = TopmostCheck.IsChecked == true;
        _settings.ShowLabels = LabelsCheck.IsChecked == true;
        _settings.TrayOnMinimize = TrayCheck.IsChecked == true;

        if (_canAssociate)
            ApplyAssociation(AssociateCheck.IsChecked == true);
        DialogResult = true;
    }

    private void ApplyAssociation(bool wanted)
    {
        var exe = Environment.ProcessPath!;
        try
        {
            if (wanted && !FileAssociation.IsApplied(exe))
                FileAssociation.Apply(exe, Loc.Get("FileTypeDescription"));
            else if (!wanted && FileAssociation.IsApplied(exe))
                FileAssociation.Remove();
        }
        catch (Exception ex)
        {
            AppLog.Write($"asociacion de .soctask: {ex}");
            PromptWindow.Alert(this, Loc.Get("FilesTitle"), Loc.Get("AssociateFailed"));
        }
    }

    private void Fail(string key) => PromptWindow.Alert(this, Loc.Get("SettingsTitle"), Loc.Get(key));
}
