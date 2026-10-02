using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using SocAutoTask.AppServices;
using SocAutoTask.Desktop;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Input;
using SocAutoTask.Localization;
using SocAutoTask.Model;
using SocAutoTask.Native;
using SocAutoTask.Playback;

namespace SocAutoTask.Tests;

/// <summary>
/// Las ventanas de verdad en un hilo STA propio, con la App y sus recursos (App.xaml). Los
/// dialogos modales se contestan con <see cref="Expect{T}"/>: al cargarse la ventana esperada se
/// ejecuta la respuesta (pulsar un boton, rellenar un campo). Un dialogo que nadie esperaba se
/// cierra y la prueba falla. Las ventanas no se activan y se abren fuera de la pantalla.
/// </summary>
internal static class Ui
{
    private static readonly Dispatcher Dispatcher;
    private static readonly Queue<(Type Type, Action<Window> Act)> Responders = new();
    private static readonly List<Exception> Errors = [];

    /// <summary>La carpeta que eligio el modo aislado (temporal) al arrancar el arnes.</summary>
    public static string SandboxFolder { get; private set; } = string.Empty;

    static Ui()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var sandboxFolder = string.Empty;
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            App.HostedByTests = true;
            // pack://application:,,, tiene que ser la app, no testhost (WPF no deja cambiarlo dos veces).
            typeof(Application).GetField("_resourceAssembly", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(null, typeof(App).Assembly);
            var app = new App();
            dispatcher.UnhandledException += (_, e) =>
            {
                Errors.Add(e.Exception);
                e.Handled = true;
            };
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // App.xaml dice OnMainWindowClose
            // El modo aislado de verdad: las ventanas no se activan al abrirse (y de paso se prueba).
            var paths = AppPaths.Current;
            Environment.SetEnvironmentVariable(Sandbox.Variable, "1");
            Sandbox.IsOn = true;
            Sandbox.Apply();
            Sandbox.IsOn = false;
            Environment.SetEnvironmentVariable(Sandbox.Variable, null);
            sandboxFolder = AppPaths.Current.DataFolder;   // aun dentro del constructor estatico: nada de Ui.X aqui
            AppPaths.Current = paths;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "Ui" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        Dispatcher = dispatcher!;
        SandboxFolder = sandboxFolder;
    }

    /// <summary>Ejecuta en el hilo de la interfaz y devuelve los fallos de los dialogos contestados.</summary>
    public static void Run(Action action) => Run(() => { action(); return 0; });

    public static T Run<T>(Func<T> action)
    {
        var done = new ManualResetEventSlim();
        var watchdog = new Thread(() =>
        {
            // Una prueba colgada (un dialogo que no se cierra) no cuelga el banco: se cierra todo y falla.
            if (done.Wait(30_000))
                return;
            Dispatcher.BeginInvoke(() =>
            {
                var open = string.Join(", ", Application.Current.Windows.Cast<Window>().Select(w => $"{w.GetType().Name} «{w.Title}» {Describe(w)}"));
                Errors.Add(new TimeoutException("La prueba no acabo en 30 s; ventanas abiertas: " + open));
                foreach (var w in Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).ToList())
                    w.Close();
            });
        }) { IsBackground = true };
        watchdog.Start();
        var result = Dispatcher.Invoke(() =>
        {
            Responders.Clear();
            Errors.Clear();
            try
            {
                return action();
            }
            finally
            {
                foreach (var w in Application.Current.Windows.Cast<Window>().ToList())
                {
                    if (w is MainWindow)
                        w.GetType().GetField("_quitRequested", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(w, true);
                    w.Close();
                }
                DoEvents();   // Unloaded va diferido: que los controles se desenganchen ya
            }
        });
        done.Set();
        var pending = Dispatcher.Invoke(() => (Responders.Count, Errors.ToList()));
        if (pending.Item2.Count > 0)
            ExceptionDispatchInfo.Capture(pending.Item2[0]).Throw();
        Assert.True(pending.Count == 0, $"Quedaron {pending.Count} dialogos esperados sin abrir");
        return result;
    }

    /// <summary>Respuestas que aun esperan a su dialogo.</summary>
    public static int Pending => Responders.Count;

    /// <summary>La proxima ventana de tipo T que se cargue recibe esta respuesta.</summary>
    public static void Expect<T>(Action<T> act) where T : Window => Responders.Enqueue((typeof(T), w => act((T)w)));

    /// <summary>Un aviso o una pregunta (PromptWindow): pulsa el boton con ese AutomationId y devuelve el texto.</summary>
    public static void Answer(string buttonId, Action<string>? message = null) => Expect<PromptWindow>(w =>
    {
        message?.Invoke(Find<TextBlock>(w, "MessageText").Text);
        Click(Find<Button>(w, buttonId));
    });

    /// <summary>Pide un numero: escribe el valor y pulsa Aceptar (o Cancelar si es null).</summary>
    public static void AnswerNumber(string? value) => Expect<PromptWindow>(w =>
    {
        if (value is null)
        {
            Click(Find<Button>(w, "CancelButton"));
            return;
        }
        Find<TextBox>(w, "ValueBox").Text = value;
        Click(Find<Button>(w, "OkButton"));
    });

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var w = (Window)sender;
        w.Left = -20000;
        w.Top = -20000;
        if (w is MainWindow && Responders.Count == 0 || w is MainWindow && Responders.Peek().Type != typeof(MainWindow))
            return;
        if (Responders.Count == 0 || !Responders.Peek().Type.IsInstanceOfType(w))
        {
            Errors.Add(new InvalidOperationException($"Dialogo inesperado: {w.GetType().Name} «{w.Title}» {Describe(w)}"));
            w.Dispatcher.BeginInvoke(w.Close);
            return;
        }
        var (_, act) = Responders.Dequeue();
        w.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                act(w);
            }
            catch (Exception ex)
            {
                Errors.Add(ex);
                w.Close();
            }
        }, DispatcherPriority.Background);
    }

    private static string Describe(Window w) =>
        w is PromptWindow ? All<TextBlock>(w).FirstOrDefault()?.Text ?? string.Empty : string.Empty;

    private static readonly System.Reflection.MethodInfo OnClick =
        typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    /// <summary>¿Se ha cerrado ya? (IsLoaded tarda en cambiar.)</summary>
    public static bool IsClosed(Window w) => PresentationSource.FromVisual(w) is null && !w.IsVisible;

    /// <summary>Pulsa un boton como lo haria el raton (evento Click).</summary>
    public static void Click(ButtonBase button)
    {
        Assert.True(button.IsEnabled, $"{button.Name} esta desactivado");
        OnClick.Invoke(button, null);   // como el raton: Click, IsCancel/IsDefault y Command
    }

    public static void Click(MenuItem item)
    {
        Assert.True(item.IsEnabled, $"{System.Windows.Automation.AutomationProperties.GetAutomationId(item)} esta desactivado");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));   // OnClick de MenuItem va diferido
    }

    /// <summary>Busca un control por x:Name o por AutomationId en el arbol logico.</summary>
    public static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        All<T>(root).FirstOrDefault(c => (c as FrameworkElement)?.Name == id || System.Windows.Automation.AutomationProperties.GetAutomationId(c) == id)
        ?? throw new InvalidOperationException($"No hay {typeof(T).Name} «{id}»");

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T t)
            yield return t;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var c in All<T>(child))
                yield return c;
    }

    /// <summary>Deja correr la cola de la interfaz hasta que se cumpla la condicion (o falla a los 10 s).</summary>
    public static void WaitFor(Func<bool> condition, int timeoutMs = 10_000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.ElapsedMilliseconds < timeoutMs, "La interfaz no llego al estado esperado");
            DoEvents();
            Thread.Sleep(5);
        }
    }

    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);   // deja pasar tambien lo de ApplicationIdle
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Lee un campo privado (estado interno que la ventana no enseña de otra forma).</summary>
    public static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;

    /// <summary>Llama a un metodo privado (manejadores de eventos que solo dispara WPF).</summary>
    public static object? Call(object target, string name, params object?[] args)
    {
        var type = target as Type ?? target.GetType();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                    (target is Type ? System.Reflection.BindingFlags.Static : System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
        var method = type.GetMethods(flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try
        {
            return method.Invoke(target is Type ? null : target, args);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}

/// <summary>Grabadora falsa: lo que se le de en <see cref="Result"/> es lo «grabado».</summary>
internal sealed class FakeRecorder : IRecorder
{
    public RecorderOptions? Options { get; set; }
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }
    public ScreenRect? Ignored { get; private set; }
    public Exception? StartError { get; set; }
    public List<MacroEvent> Events { get; set; } = [];

    public int Count => Events.Count;

    public void Start()
    {
        if (StartError is not null)
            throw StartError;
        Started = true;
    }

    public void SetIgnoredArea(ScreenRect? area) => Ignored = area;

    public Recording StopAndCollect(PlaybackOptions playback) => new(Events, playback, new ScreenRect(0, 0, 1920, 1080));

    public void Dispose() => Disposed = true;
}

internal sealed class FakeEmergency : IEmergencyWatcher
{
    public Exception? StartError { get; set; }
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    public void Start()
    {
        if (StartError is not null)
            throw StartError;
        Started = true;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Plataforma falsa para las ventanas: todo queda apuntado y nada sale del proceso.</summary>
internal sealed class FakePlatform : IPlatform, IDisposable
{
    public const string RegistryBranch = @"Software\sOCAutoTask.Tests\Ui";

    public FakePlatform()
    {
        Registry.CurrentUser.DeleteSubKeyTree(RegistryBranch, throwOnMissingSubKey: false);
        AssociationRoot = Registry.CurrentUser.CreateSubKey(RegistryBranch);
    }

    public FakeRecorder Recorder { get; set; } = new();
    public List<RecorderOptions> RecorderOptions { get; } = [];
    public RecordingSink Sink { get; set; } = new();
    public FakeClock Clock { get; set; } = new();
    public FakeEmergency Emergency { get; set; } = new();
    public (EmergencyKeys Keys, int Ms, Action Stop)? EmergencyArgs { get; private set; }
    public List<ProcessStartInfo> Started { get; } = [];
    public Exception? StartError { get; set; }
    public Queue<string?> OpenAnswers { get; } = new();
    public Queue<string?> SaveAnswers { get; } = new();
    public List<string> Pickers { get; } = [];
    public bool Elevated { get; set; }
    public bool ForegroundElevated { get; set; }
    public string? InstanceSuffix { get; set; } = "-test-" + Guid.NewGuid().ToString("N");

    public IRecorder CreateRecorder(RecorderOptions options)
    {
        RecorderOptions.Add(options);
        Recorder.Options = options;
        return Recorder;
    }

    public ICountingSink CreateSink() => Sink;

    public IClock CreateClock() => Clock;

    public IKeyboardState Keyboard { get; } = new NoModifiers();

    public IEmergencyWatcher CreateEmergency(EmergencyKeys keys, int escapeHoldMs, Action onStop)
    {
        EmergencyArgs = (keys, escapeHoldMs, onStop);
        return Emergency;
    }

    public bool IsElevated => Elevated;

    public bool ForegroundIsElevatedAndWeAreNot() => ForegroundElevated;

    public void Start(ProcessStartInfo info)
    {
        Started.Add(info);
        if (StartError is not null)
            throw StartError;
    }

    public string? PickOpenFile(Window owner, string filter, string folder)
    {
        Pickers.Add($"open|{filter}|{folder}");
        return OpenAnswers.Dequeue();
    }

    public string? PickSaveFile(Window owner, string filter, string fileName, string folder, string defaultExt)
    {
        Pickers.Add($"save|{filter}|{fileName}|{folder}|{defaultExt}");
        return SaveAnswers.Dequeue();
    }

    public RegistryKey AssociationRoot { get; set; }

    public string? ExePath { get; set; } = @"C:\Programas\sOCAutoTask\sOCAutoTask.exe";

    public bool IsPackaged { get; set; }

    public SingleInstance CreateInstance(string version) => new(version, InstanceSuffix);

    public void Dispose()
    {
        AssociationRoot.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\sOCAutoTask.Tests", throwOnMissingSubKey: false);
    }

    private sealed class NoModifiers : IKeyboardState
    {
        public bool AnyModifierDown() => false;
    }
}

/// <summary>
/// Base de las pruebas de ventanas: plataforma falsa, datos en una carpeta temporal, idioma
/// español y modo aislado apagado. Lo deja todo como estaba al acabar.
/// </summary>
public abstract class UiTest : IDisposable
{
    private readonly AppPaths _paths = AppPaths.Current;
    private readonly IPlatform _platform = Platform.Current;
    private readonly Func<Stream?> _player = MainWindow.PlayerStream;
    private readonly string _language = Loc.Language;

    internal FakePlatform Fake { get; } = new();
    internal TempFolder Temp { get; } = new();

    protected UiTest()
    {
        Platform.Current = Fake;
        AppPaths.Current = AppPaths.At(Temp.Path);
        Ui.Run(() =>
        {
            Loc.Use("es");
            Sandbox.IsOn = false;
        });
    }

    /// <summary>Ajustes de prueba: atajos raros (F13/F14) para no chocar con nada de quien trabaja.</summary>
    internal static AppSettings Settings() => new()
    {
        RecordHotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7C).ToString(),
        PlayHotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7D).ToString(),
        CountdownSeconds = 0,
        GuideShown = true,
    };

    public virtual void Dispose()
    {
        Ui.Run(() =>
        {
            Sandbox.IsOn = false;
            ((App)Application.Current).ReleaseInstance();
        });
        Platform.Current = _platform;
        AppPaths.Current = _paths;
        MainWindow.PlayerStream = _player;
        Ui.Run(() => Loc.Use(_language));
        Fake.Dispose();
        Temp.Dispose();
    }
}
