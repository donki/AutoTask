using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SocAutoTask.AppServices;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Localization;

namespace SocAutoTask.Desktop;

/// <summary>Novedades de las cinco ultimas versiones (§6.7), de la mas nueva a la mas antigua.</summary>
public sealed class WhatsNewWindow : Window
{
    private readonly StackPanel _list = new();

    public WhatsNewWindow()
    {
        Width = 520;
        Height = 520;
        MinWidth = 400;
        MinHeight = 300;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var close = new Button { Style = (Style)FindResource("IconButton"), Tag = "check", HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true, IsDefault = true };
        AutomationProperties.SetAutomationId(close, "CloseButton");
        close.Click += (_, _) => Close();
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(close, Dock.Bottom);
        close.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Build();
        Loc.LanguageChanged += Build;
        Closed += (_, _) => Loc.LanguageChanged -= Build;
    }

    private void Build()
    {
        Title = Loc.Get("WhatsNew");
        _list.Children.Clear();
        foreach (var entry in WhatsNew.Latest())
        {
            var card = new Border { Style = (Style)FindResource("Card") };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = entry.Version, Style = (Style)FindResource("CardTitle") });
            foreach (var line in Loc.Language == "es" ? entry.Spanish : entry.English)
                stack.Children.Add(new TextBlock { Text = "•  " + line, Style = (Style)FindResource("BodyText"), Margin = new Thickness(0, 6, 0, 0) });
            card.Child = stack;
            _list.Children.Add(card);
        }
    }
}
