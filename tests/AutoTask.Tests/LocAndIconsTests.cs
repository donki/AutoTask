using System.Text.RegularExpressions;
using System.Xml.Linq;
using SocAutoTask.AppServices;
using SocAutoTask.Desktop.Services;
using SocAutoTask.Format;
using SocAutoTask.Localization;
using SocAutoTask.Model;
using SocAutoTask.Playback;

namespace SocAutoTask.Tests;

/// <summary>Textos es/en (RNF-06, CA-09) e iconos SVG planos (§6.2).</summary>
public sealed partial class LocAndIconsTests
{
    private static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AutoTask.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("No se encuentra AutoTask.slnx");
    }

    private static string AppFolder => Path.Combine(Repo(), "src", "sOCAutoTask");

    [Fact]
    public void Español_EIngles_TienenLasMismasClaves_YNingunaVacia()
    {
        Assert.Empty(Loc.Spanish.Keys.Except(Loc.English.Keys));
        Assert.Empty(Loc.English.Keys.Except(Loc.Spanish.Keys));
        Assert.All(Loc.Spanish, p => Assert.False(string.IsNullOrWhiteSpace(p.Value), p.Key));
        Assert.All(Loc.English, p => Assert.False(string.IsNullOrWhiteSpace(p.Value), p.Key));
    }

    [Fact]
    public void Huecos_IgualesEnLosDosIdiomas()
    {
        foreach (var key in Loc.Spanish.Keys)
            Assert.True(Holes(Loc.Spanish[key]).SetEquals(Holes(Loc.English[key])), key);
    }

    private static HashSet<string> Holes(string text) => HoleRegex().Matches(text).Select(m => m.Groups[1].Value).ToHashSet();

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex HoleRegex();

    [Fact]
    public void TodaClaveUsada_Existe()
    {
        // Las que se piden en el codigo y el XAML (Loc.Get/Format("...") y {loc:T ...}).
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo(), "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains(@"\obj\") && !f.Contains(@"\bin\")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"Loc\.(?:Get|Format)\(""([A-Za-z_]+)""[,)]"))
                used.Add(m.Groups[1].Value);
            if (file.EndsWith(".xaml"))
                foreach (Match m in Regex.Matches(text, @"\{loc:T ([A-Za-z_]+)\}"))
                    used.Add(m.Groups[1].Value);
        }
        Assert.True(used.Count > 100, $"solo {used.Count}");
        Assert.DoesNotContain(used, k => !Loc.English.ContainsKey(k));
    }

    [Fact]
    public void ClavesCompuestas_Existen()
    {
        foreach (var kind in Enum.GetNames<EventKind>()) Assert.True(Loc.English.ContainsKey("Kind" + kind), kind);
        foreach (var button in Enum.GetNames<MouseButton>()) Assert.True(Loc.English.ContainsKey("Button" + button), button);
        foreach (var problem in Enum.GetValues<FormatProblem>()) Assert.True(Loc.English.ContainsKey(new RecordingFormatException(problem, "").LocKey), problem.ToString());
        foreach (var theme in Enum.GetNames<AppTheme>()) Assert.True(Loc.English.ContainsKey("Theme" + theme), theme);
        foreach (var step in new[] { "Done", "Pending", "Optional" }) Assert.True(Loc.English.ContainsKey("Step" + step));
        foreach (var guide in new[] { "GuideHotkeys", "GuideEmergency", "GuidePrivacy", "GuideFiles", "GuideAdmin", "GuideTry" })
        {
            Assert.True(Loc.English.ContainsKey(guide + "Title"), guide);
            Assert.True(Loc.English.ContainsKey(guide + "Text"), guide);
        }
        foreach (var reason in Enum.GetNames<StopReason>()) Assert.NotNull(reason);
    }

    [Fact]
    public void NombresAjenos_SoloLosPermitidos()
    {
        // §6.13: de productos ajenos, solo Microsoft y Google (y unos pocos mas); la utilidad en que se
        // inspira no se nombra en la aplicacion.
        foreach (var text in Loc.Spanish.Values.Concat(Loc.English.Values))
            Assert.DoesNotContain("TinyTask", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CambiarIdioma_Avisa_YDesconocidoEsIngles()
    {
        var changes = 0;
        void Count() => changes++;
        Loc.Use("en");   // punto de partida fijo: no depende de la prueba de antes
        Loc.LanguageChanged += Count;
        try
        {
            Loc.Use("es");
            Assert.Equal("Grabar", Loc.Get("Record"));
            Assert.Equal("es-ES", Loc.Culture.Name);
            Loc.Use("es");   // el mismo: no avisa
            Loc.Use("fr");
            Assert.Equal("en", Loc.Language);
            Assert.Equal("Record", Loc.Get("Record"));
            Assert.Equal(2, changes);
            Assert.Equal(string.Empty, Loc.Get("NoExisteEstaClave"));
            Assert.Contains(Loc.SystemLanguage(), new[] { "es", "en" });
        }
        finally
        {
            Loc.LanguageChanged -= Count;
            Loc.Use("en");
        }
    }

    // =====================================================================
    //  Iconos
    // =====================================================================

    private static IEnumerable<string> IconFiles() => Directory.EnumerateFiles(Path.Combine(AppFolder, "Assets", "Icons"), "ic_*.svg");

    [Fact]
    public void Iconos_TodosSeLeen_YSonPlanos()
    {
        SvgIcons.Open = name => File.OpenRead(Path.Combine(AppFolder, "Assets", "Icons", $"ic_{name}.svg"));
        var files = IconFiles().ToList();
        Assert.True(files.Count >= 30);
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file)[3..];
            var icon = SvgIcons.Get(name);
            Assert.NotEmpty(icon.Parts);
            var root = XDocument.Load(file).Root!;
            if (name.StartsWith("flag_"))
            {
                // Las banderas: el unico dibujo con color, rectangulo 24x16.
                Assert.Equal((24, 16), (icon.Width, icon.Height));
                Assert.All(icon.Parts, p => Assert.NotNull(p.Fill));
                continue;
            }
            Assert.Equal((24, 24), (icon.Width, icon.Height));
            Assert.Equal("none", root.Attribute("fill")?.Value);
            Assert.Equal("currentColor", root.Attribute("stroke")?.Value);
            Assert.Equal("1.8", root.Attribute("stroke-width")?.Value);
            Assert.Equal("round", root.Attribute("stroke-linecap")?.Value);
            Assert.All(icon.Parts, p => Assert.True(p.Stroked && p.Fill is null, name));
        }
    }

    [Fact]
    public void Iconos_TodoLoQueSeUsa_Existe()
    {
        var names = IconFiles().Select(f => Path.GetFileNameWithoutExtension(f)[3..]).ToHashSet();
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(AppFolder, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains(@"\obj\") && !f.Contains(@"\bin\") && !f.EndsWith("SvgIcons.cs")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"(?:Tag|Kind)=""([a-z_]+)"""))
                used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"Tag = ""([a-z_]+)"""))
                used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"(?:Item\(""[A-Za-z0-9]+"", |new\(""Guide[A-Za-z]+"", |\? )""([a-z_]+)"""))
                used.Add(m.Groups[1].Value);
        }
        Assert.True(used.Count > 20, string.Join(", ", used));
        Assert.DoesNotContain(used, u => !names.Contains(u));
    }

    [Fact]
    public void Iconos_FormasQueEntiende()
    {
        var svg = XDocument.Parse("""
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <g><line x1="1" y1="2" x2="3" y2="4"/></g>
              <polyline points="1,1 5,5 9,1"/>
              <polygon points="1 1 5 5 9 1"/>
              <rect x="1" y="1" width="4" height="4" rx="1"/>
              <circle cx="5" cy="5" r="2"/>
              <path d="M1 1h4"/>
              <polyline points="1"/>
              <text>no</text>
              <rect x="0" y="0" width="2" height="2" fill="#FF0000" stroke="none"/>
            </svg>
            """);
        var icon = SvgIcons.Parse(svg);
        Assert.Equal(7, icon.Parts.Count);
        Assert.NotNull(icon.Parts[^1].Fill);
        Assert.False(icon.Parts[^1].Stroked);
        Assert.Throws<FileNotFoundException>(() => { SvgIcons.Open = _ => null; SvgIcons.Get("no_existe_" + Guid.NewGuid().ToString("N")); });
    }
}
