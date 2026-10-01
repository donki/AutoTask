using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using SocAutoTask.Desktop.Services;

namespace SocAutoTask.Desktop.Controls;

/// <summary>
/// Un icono SVG plano del catalogo (§6.2): trazo de 1.8 con puntas redondas en el color del texto
/// del control que lo contiene (asi sigue al tema y al blanco de los botones rellenos). Las
/// banderas llevan su color.
/// </summary>
public sealed class Icon : Viewbox
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Icon), new PropertyMetadata(null, (d, _) => ((Icon)d).Build()));

    public Icon()
    {
        Width = 20;
        Height = 20;
        Stretch = Stretch.Uniform;
        Focusable = false;
        IsHitTestVisible = false;
    }

    public string? Kind
    {
        get => (string?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void Build()
    {
        if (string.IsNullOrEmpty(Kind))
        {
            Child = null;
            return;
        }
        var data = SvgIcons.Get(Kind);
        var canvas = new Canvas { Width = data.Width, Height = data.Height };
        foreach (var part in data.Parts)
        {
            var path = new Path
            {
                Data = part.Geometry,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };
            if (part.Fill is { } fill)
                path.Fill = new SolidColorBrush(fill);
            if (part.Stroked)
                path.SetBinding(Shape.StrokeProperty, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self });
            canvas.Children.Add(path);
        }
        Child = canvas;
        if (data.Width != data.Height)
            Height = Width * data.Height / data.Width;
    }
}
