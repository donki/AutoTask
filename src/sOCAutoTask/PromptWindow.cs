using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Localization;

namespace SocAutoTask.Desktop;

/// <summary>Respuesta a «hay cambios sin guardar».</summary>
public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// Dialogos pequeños con el aspecto de la aplicacion (§6.3): aviso, confirmacion, cambios sin
/// guardar y pedir un numero. Botones de icono con su texto en el tooltip y AutomationId fijo para
/// las pruebas (OkButton, CancelButton, DiscardButton, ValueBox).
/// </summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox? _value;
    private UnsavedChoice _choice = UnsavedChoice.Cancel;

    private PromptWindow(Window? owner, string title, string message, string? initial, bool confirm, bool alert, bool unsaved, bool danger = false)
    {
        if (owner is { IsVisible: true })
            Owner = owner;
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = owner is null;
        WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Topmost = owner?.Topmost ?? false;
        SetResourceReference(BackgroundProperty, "PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0) };
        var stack = new StackPanel();
        card.Child = stack;
        var text = new TextBlock { Text = message, Style = (Style)FindResource("BodyText") };
        AutomationProperties.SetAutomationId(text, "MessageText");
        stack.Children.Add(text);

        if (initial is not null)
        {
            _value = new TextBox { Style = (Style)FindResource("Field"), Margin = new Thickness(0, 10, 0, 0), Text = initial };
            AutomationProperties.SetAutomationId(_value, "ValueBox");
            stack.Children.Add(_value);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Button Make(string id, string icon, string tip, string style)
        {
            var b = new Button { Style = (Style)FindResource(style), Tag = icon, ToolTip = Loc.Get(tip) };
            AutomationProperties.SetAutomationId(b, id);
            AutomationProperties.SetName(b, Loc.Get(tip));
            buttons.Children.Add(b);
            return b;
        }

        if (unsaved)
        {
            Make("CancelButton", "close", "Cancel", "GhostIconButton").Click += (_, _) => { _choice = UnsavedChoice.Cancel; DialogResult = false; };
            Make("DiscardButton", "delete", "Discard", "DangerIconButton").Click += (_, _) => { _choice = UnsavedChoice.Discard; DialogResult = true; };
            var save = Make("OkButton", "save", "Save", "IconButton");
            save.IsDefault = true;
            save.Click += (_, _) => { _choice = UnsavedChoice.Save; DialogResult = true; };
        }
        else
        {
            if (!alert)
                Make("CancelButton", "close", "Cancel", "GhostIconButton").IsCancel = true;
            var ok = Make("OkButton", danger ? "delete" : "check", alert ? "Ok" : confirm ? "Yes" : "Ok", danger ? "DangerIconButton" : "IconButton");
            ok.IsDefault = true;
            ok.IsCancel = alert;
            ok.Click += (_, _) => DialogResult = true;
        }

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(card);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            if (_value is null)
                return;
            _value.Focus();
            _value.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                DialogResult = false;
        };
    }

    public static void Alert(Window? owner, string title, string message) =>
        new PromptWindow(owner, title, message, null, confirm: false, alert: true, unsaved: false).ShowDialog();

    public static bool Confirm(Window? owner, string title, string message, bool danger = false) =>
        new PromptWindow(owner, title, message, null, confirm: true, alert: false, unsaved: false, danger).ShowDialog() == true;

    public static UnsavedChoice AskUnsaved(Window? owner, string message)
    {
        var w = new PromptWindow(owner, Loc.Get("UnsavedTitle"), message, null, confirm: false, alert: false, unsaved: true);
        w.ShowDialog();
        return w._choice;
    }

    /// <summary>Pide un entero entre <paramref name="min"/> y <paramref name="max"/>. Null si se cancela; si no vale, se avisa y se vuelve a pedir (§6.8).</summary>
    public static int? AskNumber(Window? owner, string title, string message, int initial, int min, int max)
    {
        var text = initial.ToString(Loc.Culture);
        while (true)
        {
            var w = new PromptWindow(owner, title, message, text, confirm: false, alert: false, unsaved: false);
            if (w.ShowDialog() != true)
                return null;
            text = w._value!.Text.Trim();
            if (int.TryParse(text, System.Globalization.NumberStyles.Integer, Loc.Culture, out var n) && n >= min && n <= max)
                return n;
            Alert(owner, title, Loc.Format("NumberOutOfRange", min, max));
        }
    }
}
