using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace SocAutoTask.Desktop.Services;

/// <summary>
/// Lee los iconos SVG planos de <c>Assets/Icons/ic_&lt;nombre&gt;.svg</c> (constitucion general §6.2)
/// y los convierte en geometrias WPF. Entiende lo que usan los iconos del catalogo: path, line,
/// polyline, polygon, rect y circle, con fill/stroke en el elemento o heredados del raiz.
/// </summary>
public static class SvgIcons
{
    /// <summary>Una pieza del icono: su forma y si va rellena de un color fijo (banderas) o trazada con el color del texto.</summary>
    public sealed record Part(Geometry Geometry, Color? Fill, bool Stroked);

    public sealed record IconData(double Width, double Height, IReadOnlyList<Part> Parts);

    private static readonly Dictionary<string, IconData> Cache = [];

    /// <summary>Lector del recurso; las pruebas lo cambian por uno que lee del disco.</summary>
    public static Func<string, Stream?> Open { get; set; } = name =>
    {
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Icons/ic_{name}.svg"));
        return info?.Stream;
    };

    public static IconData Get(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached))
                return cached;
            using var stream = Open(name) ?? throw new FileNotFoundException($"No hay icono ic_{name}.svg");
            var data = Parse(XDocument.Load(stream));
            Cache[name] = data;
            return data;
        }
    }

    public static IconData Parse(XDocument doc)
    {
        var root = doc.Root ?? throw new FormatException("SVG vacio");
        var box = (root.Attribute("viewBox")?.Value ?? "0 0 24 24").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
        var parts = new List<Part>();
        Walk(root, root.Attribute("fill")?.Value ?? "black", root.Attribute("stroke")?.Value ?? "none", parts);
        return new IconData(box.Length == 4 ? box[2] : 24, box.Length == 4 ? box[3] : 24, parts);
    }

    private static void Walk(XElement element, string fill, string stroke, List<Part> parts)
    {
        foreach (var child in element.Elements())
        {
            var f = child.Attribute("fill")?.Value ?? fill;
            var s = child.Attribute("stroke")?.Value ?? stroke;
            if (child.Name.LocalName == "g")
            {
                Walk(child, f, s, parts);
                continue;
            }
            var geometry = ToGeometry(child);
            if (geometry is null)
                continue;
            geometry.Freeze();
            Color? fillColor = f is "none" or "currentColor" ? null : (Color)ColorConverter.ConvertFromString(f);
            parts.Add(new Part(geometry, fillColor, s != "none"));
        }
    }

    private static Geometry? ToGeometry(XElement e)
    {
        double A(string name) => Number(e.Attribute(name)?.Value ?? "0");
        switch (e.Name.LocalName)
        {
            case "path":
                return Geometry.Parse(e.Attribute("d")?.Value ?? string.Empty);
            case "line":
                return new LineGeometry(new Point(A("x1"), A("y1")), new Point(A("x2"), A("y2")));
            case "rect":
                return new RectangleGeometry(new Rect(A("x"), A("y"), A("width"), A("height")), A("rx"), e.Attribute("ry") is null ? A("rx") : A("ry"));
            case "circle":
                return new EllipseGeometry(new Point(A("cx"), A("cy")), A("r"), A("r"));
            case "polyline" or "polygon":
                var numbers = (e.Attribute("points")?.Value ?? string.Empty).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
                if (numbers.Length < 4)
                    return null;
                var figure = new PathFigure { StartPoint = new Point(numbers[0], numbers[1]), IsClosed = e.Name.LocalName == "polygon" };
                for (var i = 2; i + 1 < numbers.Length; i += 2)
                    figure.Segments.Add(new LineSegment(new Point(numbers[i], numbers[i + 1]), true));
                return new PathGeometry([figure]);
            default:
                return null;
        }
    }

    private static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
