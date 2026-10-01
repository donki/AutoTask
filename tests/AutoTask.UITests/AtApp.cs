using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace SocAutoTask.UITests;

/// <summary>
/// Una instancia de sOC AutoTask lanzada para una prueba: el exe Debug en modo aislado
/// (SOC_SANDBOX con una carpeta temporal nueva), con su ventana y utilidades para esperar
/// ventanas, buscar controles y guardar capturas. Como en RC Manager (RcApp).
/// </summary>
/// <remarks>
/// Todo va por patrones de UI Automation (Invoke, Value, SelectionItem, Toggle): no se mueve el
/// raton ni se teclea. En modo aislado la aplicacion no graba ni reproduce aunque se le pida.
/// </remarks>
public sealed class AtApp : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _testName;
    private int _shot;

    public Application App { get; }
    public UIA3Automation Automation { get; } = new();
    public FlaUI.Core.AutomationElements.Window Main { get; }
    public ConditionFactory Cf => Automation.ConditionFactory;
    public string DataFolder { get; }

    private AtApp(string testName, string? language, string[] args)
    {
        _testName = testName;
        DataFolder = Path.Combine(Path.GetTempPath(), "sOCAutoTask-uitests", $"{testName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataFolder);
        // Ajustes de partida: idioma fijo (las pruebas no dependen del de Windows).
        File.WriteAllText(Path.Combine(DataFolder, "settings.json"),
            $$"""{ "Language": "{{language ?? "es"}}", "GuideShown": true, "LastSeenVersion": "{{SocAutoTask.AppServices.AppInfo.Version}}" }""");

        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(ExePath)!,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["SOC_SANDBOX"] = DataFolder;
        App = Application.Launch(psi);
        Main = Retry.WhileNull(() => App.GetMainWindow(Automation, TimeSpan.FromSeconds(1)), Timeout, throwOnTimeout: true).Result!;
        Main.WaitUntilClickable(Timeout);
    }

    public static AtApp Launch(string? language = "es", string[]? args = null, [System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        new(testName, language, args ?? []);

    // =====================================================================
    //  Rutas
    // =====================================================================

    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>El exe Debug de la aplicacion; AUTOTASK_EXE lo cambia por otro (tiene que ser Debug).</summary>
    public static string ExePath
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("AUTOTASK_EXE") is { Length: > 0 } custom
                ? custom
                : Path.Combine(RepoRoot, "src", "sOCAutoTask", "bin", "Debug", "net10.0-windows", "sOCAutoTask.exe");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No esta el exe: compila antes en Debug (dotnet build src\\sOCAutoTask -c Debug). Buscado en {path}");
            return path;
        }
    }

    public static string ArtifactsFolder { get; } = Directory.CreateDirectory(Path.Combine(RepoRoot, "tests", "AutoTask.UITests", "artifacts")).FullName;

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AutoTask.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("No se encuentra AutoTask.slnx subiendo desde " + AppContext.BaseDirectory);
    }

    // =====================================================================
    //  Buscar y esperar
    // =====================================================================

    public AutomationElement ById(AutomationElement parent, string id) =>
        Retry.WhileNull(() => parent.FindFirstDescendant(Cf.ByAutomationId(id)), Timeout, throwOnTimeout: true,
            timeoutMessage: $"No aparece el control {id}").Result!;

    public FlaUI.Core.AutomationElements.Button Button(AutomationElement parent, string id) => ById(parent, id).AsButton();

    public string Text(AutomationElement parent, string id) => ById(parent, id).Name;

    public FlaUI.Core.AutomationElements.Window WaitModal()
    {
        var modal = Retry.WhileNull(() => Main.ModalWindows.FirstOrDefault(), Timeout, throwOnTimeout: true, timeoutMessage: "No se abre el dialogo").Result!;
        modal.WaitUntilClickable(Timeout);
        return modal;
    }

    public FlaUI.Core.AutomationElements.Window WaitModal(FlaUI.Core.AutomationElements.Window owner)
    {
        var modal = Retry.WhileNull(() => owner.ModalWindows.FirstOrDefault(), Timeout, throwOnTimeout: true, timeoutMessage: "No se abre el dialogo").Result!;
        modal.WaitUntilClickable(Timeout);
        return modal;
    }

    public void WaitNoModal() =>
        Retry.WhileTrue(() => Main.ModalWindows.Length > 0, Timeout, throwOnTimeout: true, timeoutMessage: "El dialogo no se cierra");

    public static void Press(AutomationElement button) => button.Patterns.Invoke.Pattern.Invoke();

    /// <summary>Abre el menu «Mas» y pulsa una de sus entradas (por su AutomationId).</summary>
    public void MenuItem(string id)
    {
        Press(Button(Main, "MoreButton"));
        var item = Retry.WhileNull(() => Automation.GetDesktop().FindFirstDescendant(Cf.ByAutomationId(id)), Timeout, throwOnTimeout: true,
            timeoutMessage: $"No aparece la entrada {id} del menu").Result!;
        item.Patterns.Invoke.Pattern.Invoke();
    }

    public bool WaitText(AutomationElement parent, string id, Func<string, bool> condition) =>
        Retry.WhileFalse(() => condition(Text(parent, id)), Timeout).Result;

    // =====================================================================
    //  Capturas (PrintWindow: sin robar el foco, aunque este tapada)
    // =====================================================================

    public string Capture(AutomationElement window, string step) => CaptureWindow(window.Properties.NativeWindowHandle.Value, $"{_testName}-{++_shot:00}-{step}");

    public static string CaptureWindow(IntPtr hwnd, string name)
    {
        GetWindowRect(hwnd, out var r);
        var (w, h) = (Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            try { PrintWindow(hwnd, hdc, 2); }
            finally { g.ReleaseHdc(hdc); }
        }
        var file = Path.Combine(ArtifactsFolder, name + ".png");
        bmp.Save(file, ImageFormat.Png);
        return file;
    }

    public void Dispose()
    {
        try
        {
            foreach (var modal in Main.ModalWindows)
                modal.Close();
            App.Close();
            var process = Process.GetProcessById(App.ProcessId);
            if (!process.WaitForExit(5000))
                process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
        }
        catch (Exception)
        {
            try { Process.GetProcessById(App.ProcessId).Kill(entireProcessTree: true); } catch { }
        }
        App.Dispose();
        Automation.Dispose();
        try { Directory.Delete(DataFolder, recursive: true); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
