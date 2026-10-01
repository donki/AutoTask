using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using SocAutoTask.AppServices;

namespace SocAutoTask.Desktop.Services;

/// <summary>
/// Tema claro u oscuro (RF-44): el de Windows o el elegido. Cambia los pinceles de App.xaml (todo
/// los usa con DynamicResource) y la barra de titulo de las ventanas abiertas, en caliente.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark { get; private set; }

    public static AppTheme Mode { get; private set; } = AppTheme.System;

    public static void Apply(AppTheme mode)
    {
        Mode = mode;
        IsDark = mode switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemPrefersDark(),
        };
        var r = Application.Current.Resources;
        if (IsDark)
        {
            Set(r, "PageBackground", "#141318");
            Set(r, "CardBackground", "#201F27");
            Set(r, "Separator", "#48454F");
            Set(r, "TextPrimary", "#E6E1E9");
            Set(r, "TextSecondary", "#C7C4D8");
            Set(r, "WarningSurface", "#33291A");
            Set(r, "Primary", "#8B83FF");
            Set(r, "PrimaryFill", "#3525CD");
            Set(r, "Selection", "#2E2A55");
        }
        else
        {
            Set(r, "PageBackground", "#F8F9FA");
            Set(r, "CardBackground", "#FFFFFF");
            Set(r, "Separator", "#C7C4D8");
            Set(r, "TextPrimary", "#191C1D");
            Set(r, "TextSecondary", "#464555");
            Set(r, "WarningSurface", "#FFF4E5");
            Set(r, "Primary", "#3525CD");
            Set(r, "PrimaryFill", "#3525CD");
            Set(r, "Selection", "#E3E0FF");
        }
        foreach (Window w in Application.Current.Windows)
            ApplyToWindow(w);
    }

    /// <summary>Windows cambio de tema: si se sigue al sistema, se vuelve a aplicar.</summary>
    public static void WatchSystem()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Mode == AppTheme.System)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
        };
    }

    /// <summary>La barra de titulo la pinta Windows: se le pide que siga al tema (DWM).</summary>
    public static void ApplyToWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Set(ResourceDictionary resources, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        resources[key] = brush;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
