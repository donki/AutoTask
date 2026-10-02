using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using SocAutoTask.AppServices;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Localization;

namespace SocAutoTask.Desktop;

/// <summary>«Acerca de»: version, contacto, idioma (en caliente), privacidad, licencias, novedades y aviso legal.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        LogoImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        VersionLabel.Text = "v" + AppInfo.Version + (Elevation() ? " · " + Loc.Get("RunningAsAdmin") : string.Empty);
        PaintLanguageButtons();
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
    }

    private static bool Elevation() => Platform.Current.IsElevated;

    private void OnLanguageChanged()
    {
        PaintLanguageButtons();
        VersionLabel.Text = "v" + AppInfo.Version + (Elevation() ? " · " + Loc.Get("RunningAsAdmin") : string.Empty);
    }

    private void PaintLanguageButtons()
    {
        var spanish = Loc.Language == "es";
        SpanishButton.Style = (Style)FindResource(spanish ? "FilledButton" : "OutlineButton");
        EnglishButton.Style = (Style)FindResource(spanish ? "OutlineButton" : "FilledButton");
    }

    private void OnSpanish(object sender, RoutedEventArgs e) => Use("es");

    private void OnEnglish(object sender, RoutedEventArgs e) => Use("en");

    private void Use(string language)
    {
        Loc.Use(language);
        if (Owner is MainWindow main)
            main.SetLanguage(language);
    }

    private void OnContact(object sender, RoutedEventArgs e)
    {
        try
        {
            Platform.Current.Start(new ProcessStartInfo(AppInfo.ContactAddress) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo abrir el correo: {ex.Message}");
            PromptWindow.Alert(this, Loc.Get("Contact"), Loc.Get("ContactFailed"));
        }
    }

    private void OnWhatsNew(object sender, RoutedEventArgs e) => new WhatsNewWindow { Owner = this }.ShowDialog();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
