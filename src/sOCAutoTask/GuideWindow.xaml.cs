using System.Windows;
using System.Windows.Media;
using SocAutoTask.AppServices;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Input;
using SocAutoTask.Localization;
using SocAutoTask.Native;

namespace SocAutoTask.Desktop;

/// <summary>Estado de un paso de la guia.</summary>
public enum StepStatus
{
    Done,
    Pending,
    Optional,
}

/// <summary>Guia de configuracion (§6.10). El estado de cada paso se vuelve a mirar al activarse la ventana.</summary>
public partial class GuideWindow : Window
{
    private sealed record Step(string Key, string Icon, Func<StepStatus> Status, string? ActionKey, Action? Action);

    private readonly MainWindow _main;
    private readonly AppSettings _settings;
    private readonly List<Step> _steps;
    private int _index;

    public GuideWindow(MainWindow main, AppSettings settings)
    {
        _main = main;
        _settings = settings;
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        _steps =
        [
            new("GuideHotkeys", "keyboard", () => _main.HotkeysOk ? StepStatus.Done : StepStatus.Pending, "GuideOpenSettings", OpenSettings),
            new("GuideEmergency", "stop", () => _settings.Emergency != EmergencyKeys.None ? StepStatus.Done : StepStatus.Pending, "GuideOpenSettings", OpenSettings),
            new("GuidePrivacy", "warning", () => StepStatus.Optional, "GuideOpenSettings", OpenSettings),
            new("GuideFiles", "folder", AssociationStatus, "GuideAssociate", Associate),
            new("GuideAdmin", "shield", () => Platform.Current.IsElevated ? StepStatus.Done : StepStatus.Optional, "RestartAsAdmin", RestartAsAdmin),
            new("GuideTry", "record", () => StepStatus.Optional, null, null),
        ];
        Activated += (_, _) => Show(_index);
        Loc.LanguageChanged += Refresh;
        Closed += (_, _) => Loc.LanguageChanged -= Refresh;
        Show(0);
    }

    private void Refresh() => Show(_index);

    private void Show(int index)
    {
        _index = Math.Clamp(index, 0, _steps.Count - 1);
        var step = _steps[_index];
        StepIcon.Kind = step.Icon;
        StepTitle.Text = Loc.Get(step.Key + "Title");
        StepText.Text = step.Key == "GuideHotkeys"
            ? Loc.Format("GuideHotkeysText", _settings.RecordKey.Display(), _settings.PlayKey.Display())
            : Loc.Get(step.Key + "Text");
        var status = step.Status();
        StatusLabel.Text = Loc.Get("Step" + status);
        StatusChip.Background = (Brush)FindResource(status switch
        {
            StepStatus.Done => "Success",
            StepStatus.Pending => "Danger",
            _ => "TextSecondary",
        });
        ActionButton.Visibility = step.ActionKey is null || (status == StepStatus.Done && step.Key is "GuideFiles" or "GuideAdmin") ? Visibility.Collapsed : Visibility.Visible;
        if (step.ActionKey is not null)
            ActionButton.Content = Loc.Get(step.ActionKey);
        StepCounter.Text = Loc.Format("GuideStep", _index + 1, _steps.Count);
        BackButton.IsEnabled = _index > 0;
        NextButton.Tag = _index == _steps.Count - 1 ? "check" : "next";
        NextButton.ToolTip = Loc.Get(_index == _steps.Count - 1 ? "GuideDone" : "GuideNext");
    }

    private StepStatus AssociationStatus()
    {
        var platform = Platform.Current;
        if (platform.IsPackaged)
            return StepStatus.Done;
        if (Sandbox.IsOn || platform.ExePath is not { } exe)
            return StepStatus.Optional;
        return FileAssociation.IsApplied(exe, platform.AssociationRoot) ? StepStatus.Done : StepStatus.Optional;
    }

    private void OnBack(object sender, RoutedEventArgs e) => Show(_index - 1);

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_index == _steps.Count - 1)
            Close();
        else
            Show(_index + 1);
    }

    private void OnAction(object sender, RoutedEventArgs e)
    {
        _steps[_index].Action?.Invoke();
        Show(_index);
    }

    private void OpenSettings() => _main.OpenSettings();

    private void Associate()
    {
        var platform = Platform.Current;
        if (Sandbox.IsOn || platform.IsPackaged || platform.ExePath is not { } exe)
            return;
        try
        {
            FileAssociation.Apply(exe, Loc.Get("FileTypeDescription"), platform.AssociationRoot);
        }
        catch (Exception ex)
        {
            AppLog.Write($"asociacion de .soctask: {ex}");
            PromptWindow.Alert(this, Loc.Get("FilesTitle"), Loc.Get("AssociateFailed"));
        }
    }

    private void RestartAsAdmin()
    {
        Close();
        _main.RestartAsAdmin();
    }
}
