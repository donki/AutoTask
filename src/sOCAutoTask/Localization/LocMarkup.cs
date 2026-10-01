using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using SocAutoTask.Localization;

namespace SocAutoTask.Desktop.Localization;

/// <summary>
/// Puente de <see cref="Loc"/> para los enlaces del XAML: al cambiar de idioma avisa de que ha
/// cambiado el indexador y todos los textos enlazados se retraducen solos (RF-44, en caliente).
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource() => Loc.LanguageChanged += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public string this[string key] => Loc.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary><c>{loc:T Clave}</c> en el XAML: un enlace al texto, que cambia con el idioma.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
