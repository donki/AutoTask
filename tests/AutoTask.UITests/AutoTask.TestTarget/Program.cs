using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SocAutoTask.TestTarget;

/// <summary>
/// AutoTask.TestTarget.exe [x y]: ventana de 420x220 en (x, y) (pixeles logicos), siempre encima,
/// con una casilla (TargetBox) y un boton (CountButton) que cuenta clics en CountText.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application();
        var box = new TextBox { Margin = new Thickness(12), Height = 40, FontSize = 18 };
        AutomationProperties.SetAutomationId(box, "TargetBox");
        var count = 0;
        var label = new TextBlock { Text = "0", FontSize = 18, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(label, "CountText");
        var button = new Button { Content = "Contar", Width = 160, Height = 60, Margin = new Thickness(12), FontSize = 18 };
        AutomationProperties.SetAutomationId(button, "CountButton");
        button.Click += (_, _) => label.Text = (++count).ToString();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(button);
        row.Children.Add(label);
        var stack = new StackPanel();
        stack.Children.Add(box);
        stack.Children.Add(row);
        var window = new Window
        {
            Title = "AutoTask TestTarget",
            Width = 420,
            Height = 220,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = args.Length > 1 && double.TryParse(args[0], out var x) ? x : 200,
            Top = args.Length > 1 && double.TryParse(args[1], out var y) ? y : 200,
            Content = stack,
        };
        app.Run(window);
    }
}
