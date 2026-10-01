using System.IO;
using System.Windows;
using SocAutoTask.AppServices;

namespace SocAutoTask.Desktop.Services;

/// <summary>
/// Modo aislado de las pruebas de interfaz (§8.4, §8.7), solo en Debug: con <c>SOC_SANDBOX</c> los
/// datos van a otra carpeta (la ruta que traiga si es absoluta, o una temporal) y la aplicacion no
/// se registra en nada compartido: ni instancia unica, ni atajos globales, ni bandeja, ni
/// asociacion de ficheros. Las ventanas no se activan al abrirse y el titulo lo dice. Y nunca
/// reproduce: Reproducir solo deja un aviso (no se puede mover el raton de quien trabaja).
/// </summary>
public static class Sandbox
{
    public const string Variable = "SOC_SANDBOX";

#if DEBUG
    public static bool IsOn { get; } = Environment.GetEnvironmentVariable(Variable) is { Length: > 0 };
#else
    public static bool IsOn => false;
#endif

    public static void Apply()
    {
        if (!IsOn)
            return;
        var value = Environment.GetEnvironmentVariable(Variable)!.Trim();
        var folder = Path.IsPathFullyQualified(value) ? value : Path.Combine(Path.GetTempPath(), "sOCAutoTask-sandbox");
        Directory.CreateDirectory(folder);
        AppPaths.Current = AppPaths.At(folder);

        foreach (var type in typeof(Sandbox).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Window)) && !t.IsAbstract))
            Window.ShowActivatedProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(false));
    }
}
