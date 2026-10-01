using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using SocAutoTask.Format;
using SocAutoTask.Model;
using SocAutoTask.Native;

namespace SocAutoTask.UITests;

/// <summary>
/// Recorridos de la interfaz sobre el exe Debug en modo aislado (§8.7, CA-09, CA-10). Cada prueba
/// arranca su instancia con una carpeta de datos vacia. Nunca se graba ni se reproduce: en modo
/// aislado la aplicacion no pone ganchos ni envia nada, y las pruebas lo comprueban.
/// </summary>
public sealed class MainWindowTests
{
    /// <summary>Una grabacion de prueba en un .soctask temporal (sin nada que reproducir de verdad).</summary>
    private static string SampleFile(string folder)
    {
        var events = new List<MacroEvent>();
        for (var i = 0; i < 30; i++)
            events.Add(MacroEvent.Move(100 + i, 100 + i, 10));
        events.Add(MacroEvent.Down(MouseButton.Left, 130, 130, 50));
        events.Add(MacroEvent.Up(MouseButton.Left, 130, 130, 60));
        events.Add(MacroEvent.Key(true, 0x41, 0x1E, delay: 100));
        events.Add(MacroEvent.Key(false, 0x41, 0x1E, delay: 80));
        var path = Path.Combine(folder, "prueba-ui.soctask");
        RecordingFile.Save(new Recording(events), path);
        return path;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sOCAutoTask-uitests", "files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Arranca_EnModoAislado_ConLaBarra()
    {
        using var app = AtApp.Launch();
        Assert.Contains("[SOC_SANDBOX]", app.Main.Title);
        foreach (var id in new[] { "OpenButton", "SaveButton", "RecordButton", "PlayButton", "CompileButton", "EditorButton", "SettingsButton", "MoreButton" })
            Assert.True(app.Button(app.Main, id).IsAvailable, id);
        // Sin grabacion: guardar, compilar, editar y reproducir, desactivados.
        Assert.False(app.Button(app.Main, "SaveButton").IsEnabled);
        Assert.False(app.Button(app.Main, "CompileButton").IsEnabled);
        Assert.False(app.Button(app.Main, "EditorButton").IsEnabled);
        Assert.False(app.Button(app.Main, "PlayButton").IsEnabled);
        Assert.Equal("Grabar", app.Button(app.Main, "RecordButton").Name);
        app.Capture(app.Main, "principal");
    }

    [Fact]
    public void Abrir_PorLineaDeOrdenes_YEditar()
    {
        var dir = TempDir();
        var file = SampleFile(dir);
        using var app = AtApp.Launch(args: [file]);
        Assert.True(app.WaitText(app.Main, "InfoText", t => t.Contains("prueba-ui") && t.Contains("34")), app.Text(app.Main, "InfoText"));
        Assert.True(app.Button(app.Main, "EditorButton").IsEnabled);
        app.Capture(app.Main, "abierta");

        AtApp.Press(app.Button(app.Main, "EditorButton"));
        var editor = app.WaitModal();
        var grid = app.ById(editor, "Grid").AsGrid();
        Assert.Equal(34, grid.RowCount);
        app.Capture(editor, "editor");

        // Simplificar los 30 movimientos en linea: queda el ultimo antes del clic (y el primero).
        AtApp.Press(app.Button(editor, "SimplifyButton"));
        Assert.True(Retry.WhileFalse(() => grid.RowCount == 6, AtApp.Timeout).Result, $"filas: {grid.RowCount}");
        // Borrar la primera fila.
        grid.Select(0);
        AtApp.Press(app.Button(editor, "DeleteButton"));
        Assert.True(Retry.WhileFalse(() => grid.RowCount == 5, AtApp.Timeout).Result);
        // Deshacer y volver a borrar.
        AtApp.Press(app.Button(editor, "UndoButton"));
        Assert.True(Retry.WhileFalse(() => grid.RowCount == 6, AtApp.Timeout).Result);
        grid.Select(0);
        AtApp.Press(app.Button(editor, "DeleteButton"));
        app.Capture(editor, "editado");
        AtApp.Press(app.Button(editor, "ApplyButton"));
        app.WaitNoModal();

        Assert.True(app.WaitText(app.Main, "InfoText", t => t.Contains("prueba-ui *") && t.Contains(" 5 ")), app.Text(app.Main, "InfoText"));
        app.Capture(app.Main, "aplicado");
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Reproducir_EnModoAislado_NoMueveNada()
    {
        var dir = TempDir();
        using var app = AtApp.Launch(args: [SampleFile(dir)]);
        Assert.True(app.WaitText(app.Main, "InfoText", t => t.Contains("prueba-ui")));
        var before = Screens.Cursor();
        AtApp.Press(app.Button(app.Main, "PlayButton"));
        Assert.True(app.WaitText(app.Main, "StatusText", t => t.Contains("SOC_SANDBOX")), app.Text(app.Main, "StatusText"));
        Thread.Sleep(600);
        Assert.Equal(before, Screens.Cursor());
        AtApp.Press(app.Button(app.Main, "RecordButton"));
        Assert.True(app.WaitText(app.Main, "StatusText", t => t.Contains("no se graba")));
        app.Capture(app.Main, "aislado");
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Ajustes_SeAbren_ValidanYSeCierran()
    {
        using var app = AtApp.Launch();
        AtApp.Press(app.Button(app.Main, "SettingsButton"));
        var settings = app.WaitModal();
        foreach (var id in new[] { "SpeedCombo", "TimesRadio", "RecordHotkeyBox", "PlayHotkeyBox", "PauseKeyCheck", "EscapeCheck", "KeyboardCheck", "TopmostCheck", "LabelsCheck", "SpanishButton", "AssociateCheck" })
            Assert.True(app.ById(settings, id).IsAvailable, id);
        Assert.False(app.ById(settings, "AssociateCheck").IsEnabled);   // en modo aislado no se toca el registro
        app.Capture(settings, "ajustes");

        // Un numero de veces que no vale: avisa y no se cierra (§6.8).
        app.ById(settings, "TimesRadio").Patterns.SelectionItem.Pattern.Select();
        app.ById(settings, "TimesBox").AsTextBox().Text = "0";
        AtApp.Press(app.Button(settings, "SaveButton"));
        var alert = app.WaitModal(settings);
        Assert.Contains("100000", app.Text(alert, "MessageText"));
        app.Capture(alert, "aviso");
        AtApp.Press(app.Button(alert, "OkButton"));

        // Bien: 3 veces, y la barra lo enseña.
        app.ById(settings, "TimesBox").AsTextBox().Text = "3";
        AtApp.Press(app.Button(settings, "SaveButton"));
        app.WaitNoModal();
        var json = File.ReadAllText(Path.Combine(app.DataFolder, "settings.json"));
        Assert.Contains("\"Times\": 3", json);
    }

    [Fact]
    public void Idioma_CambiaEnCaliente()
    {
        using var app = AtApp.Launch("es");
        Assert.Equal("Grabar", app.Button(app.Main, "RecordButton").Name);
        app.MenuItem("AboutItem");
        var about = app.WaitModal();
        app.Capture(about, "acerca-de");
        AtApp.Press(app.Button(about, "EnglishButton"));
        Assert.True(Retry.WhileFalse(() => app.Button(app.Main, "RecordButton").Name == "Record", AtApp.Timeout).Result);
        Assert.Equal("About", about.Title);
        app.Capture(app.Main, "en");
        AtApp.Press(app.Button(about, "SpanishButton"));
        Assert.True(Retry.WhileFalse(() => app.Button(app.Main, "RecordButton").Name == "Grabar", AtApp.Timeout).Result);
        AtApp.Press(app.Button(about, "CloseButton"));
        app.WaitNoModal();
        Assert.Contains("\"es\"", File.ReadAllText(Path.Combine(app.DataFolder, "settings.json")));
    }

    [Fact]
    public void Guia_YNovedades_DesdeElMenu()
    {
        using var app = AtApp.Launch();
        app.MenuItem("GuideItem");
        var guide = app.WaitModal();
        var titles = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            titles.Add(app.Text(guide, "StepTitle"));
            app.Capture(guide, $"paso-{i + 1}");
            AtApp.Press(app.Button(guide, "NextButton"));
        }
        app.WaitNoModal();
        Assert.Equal(6, titles.Distinct().Count());
        Assert.Equal("Atajos globales", titles[0]);

        app.MenuItem("WhatsNewItem");
        var news = app.WaitModal();
        Assert.Contains(SocAutoTask.AppServices.AppInfo.Version, news.FindAllDescendants(app.Cf.ByControlType(ControlType.Text)).Select(t => t.Name));
        app.Capture(news, "novedades");
        AtApp.Press(app.Button(news, "CloseButton"));
        app.WaitNoModal();
    }

    [Fact]
    public void GuardarComo_YVolverAAbrir()
    {
        var dir = TempDir();
        using var app = AtApp.Launch(args: [SampleFile(dir)]);
        Assert.True(app.WaitText(app.Main, "InfoText", t => t.Contains("prueba-ui")));
        var target = Path.Combine(dir, "copia-ui.soctask");
        app.MenuItem("SaveAsItem");
        // Dialogo comun de Windows: la casilla del nombre es 1148 y «Guardar» es 1.
        var found = Retry.WhileNull(() =>
        {
            var d = app.Main.ModalWindows.FirstOrDefault(w => w.Properties.ClassName.ValueOrDefault == "#32770");
            var edit = d?.FindFirstDescendant(app.Cf.ByAutomationId("1001").Or(app.Cf.ByAutomationId("1148")).And(app.Cf.ByControlType(ControlType.Edit)));
            return edit is null ? null : Tuple.Create(d!, edit);
        }, TimeSpan.FromSeconds(30), throwOnTimeout: true, ignoreException: true, timeoutMessage: "No se abre el dialogo de guardar").Result!;
        found.Item2.Patterns.Value.Pattern.SetValue(target);
        AtApp.Press(found.Item1.FindFirstChild(app.Cf.ByAutomationId("1"))!);
        Assert.True(Retry.WhileFalse(() => File.Exists(target), AtApp.Timeout).Result, "no se ha guardado");
        Assert.True(app.WaitText(app.Main, "InfoText", t => t.StartsWith("copia-ui ·")), app.Text(app.Main, "InfoText"));
        Assert.Equal(34, RecordingFile.Load(target).Count);
        app.Capture(app.Main, "guardada");
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void FicheroMalo_SeExplicaEnSuIdioma()
    {
        var dir = TempDir();
        var bad = Path.Combine(dir, "rota.soctask");
        File.WriteAllText(bad, "esto no es una grabacion");
        using var app = AtApp.Launch(args: [bad]);
        var alert = app.WaitModal();
        Assert.Contains("No es una grabación de AutoTask", app.Text(alert, "MessageText"));
        app.Capture(alert, "fichero-malo");
        AtApp.Press(app.Button(alert, "OkButton"));
        app.WaitNoModal();
        try { Directory.Delete(dir, true); } catch { }
    }
}
