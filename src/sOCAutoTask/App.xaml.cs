using System.Diagnostics;
using System.IO;
using System.Windows;
using SocAutoTask.AppServices;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Localization;

namespace SocAutoTask.Desktop;

public partial class App : Application
{
    private SingleInstance? _instance;
    private bool _showingError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Debug con SOC_SANDBOX: datos en otra carpeta y sin ganchos, atajos, bandeja ni instancia unica.
        Sandbox.Apply();

        // Un error que no se esperaba no cierra la aplicacion (§6.12): registro con la traza y
        // aviso en el idioma del usuario. Enganchado antes de abrir ninguna ventana.
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Write($"error no controlado: {ex.Exception}");
            ex.Handled = true;
            ShowUnexpectedError();
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLog.Write($"error no controlado en tarea: {ex.Exception}");
            ex.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Write($"error fatal: {ex.ExceptionObject}");

        var settings = AppSettings.Load(AppPaths.Current.SettingsFile);
        Loc.Use(settings.Language ?? Loc.SystemLanguage());
        ThemeManager.Apply(settings.Theme);
        ThemeManager.WatchSystem();

        var (file, play, waitPid) = CommandLine.Parse(e.Args);
        if (waitPid is { } pid)
            WaitForExit(pid);

        if (!Sandbox.IsOn)
        {
            _instance = new SingleInstance(AppInfo.Version);
            if (!_instance.Claim(file ?? string.Empty))
            {
                Shutdown();
                return;
            }
        }

        var window = new MainWindow(settings);
        MainWindow = window;
        if (_instance is not null)
        {
            _instance.ShowRequested += f => Dispatcher.BeginInvoke(() => window.BringToFront(f));
            _instance.QuitRequested += () => Dispatcher.BeginInvoke(window.QuitForNewerVersion);
        }
        window.Show();
        window.RecoverIfAny();
        if (file is not null)
            window.OpenFile(file);
        if (play && window.Session.HasRecording)
            window.Dispatcher.BeginInvoke(window.TogglePlayback, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // Guia la primera vez (§6.10); novedades al estrenar version (§6.7). No en modo aislado:
        // las pruebas las abren desde el menu.
        if (!Sandbox.IsOn)
        {
            if (!settings.GuideShown)
            {
                settings.GuideShown = true;
                settings.LastSeenVersion = AppInfo.Version;
                window.SaveSettings();
                window.Dispatcher.BeginInvoke(window.ShowGuide, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            else if (WhatsNew.ShouldShow(settings.LastSeenVersion, AppInfo.Version))
            {
                settings.LastSeenVersion = AppInfo.Version;
                window.SaveSettings();
                window.Dispatcher.BeginInvoke(() => new WhatsNewWindow { Owner = window }.ShowDialog(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseInstance();
        base.OnExit(e);
    }

    /// <summary>Suelta la instancia unica (al salir, o antes de reiniciar como administrador).</summary>
    public void ReleaseInstance()
    {
        _instance?.Dispose();
        _instance = null;
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            // Ya salio.
        }
        catch (Exception ex)
        {
            AppLog.Write($"--wait-pid {pid}: {ex.Message}");
        }
    }

    private void ShowUnexpectedError()
    {
        if (_showingError)
            return;
        _showingError = true;
        try
        {
            PromptWindow.Alert(MainWindow, Loc.Get("UnexpectedErrorTitle"), Loc.Format("UnexpectedErrorText", AppLog.FilePath));
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo enseñar el aviso de error: {ex}");
        }
        finally
        {
            _showingError = false;
        }
    }
}
