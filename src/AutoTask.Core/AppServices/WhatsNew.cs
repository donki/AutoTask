namespace SocAutoTask.AppServices;

/// <summary>
/// Novedades (§6.7): lo que cambio en las cinco ultimas versiones, escrito para quien la usa, en
/// los dos idiomas. Sale del CHANGELOG; al entregar una version se añade arriba.
/// </summary>
public static class WhatsNew
{
    public const int Shown = 5;

    public sealed record Entry(string Version, string[] Spanish, string[] English);

    public static readonly Entry[] Entries =
    [
        new("2026.10.03.0",
            [
                "Más pruebas automáticas: ahora también de las ventanas y del reproductor de los .exe (301 pruebas).",
                "Sin cambios en cómo se usa.",
            ],
            [
                "More automated tests: now also of the windows and of the .exe player (301 tests).",
                "Nothing changes in how you use it.",
            ]),
        new("2026.10.01.0",
            [
                "Primera versión: graba lo que haces con el ratón y el teclado y lo repite cuando quieras.",
                "Atajos globales Ctrl+Alt+Mayús+R (grabar) y Ctrl+Alt+Mayús+P (reproducir), configurables.",
                "Parada de emergencia con Pausa, Bloq Despl o manteniendo Esc.",
                "Velocidad de 0,5× a 100× o la que elijas, y repetir una vez, N veces o sin fin.",
                "Guarda y abre grabaciones .soctask, e importa .rec (experimental).",
                "Convierte una grabación en un .exe que la reproduce en cualquier PC.",
                "Editor de eventos: borrar, recortar, simplificar movimientos, cambiar o insertar esperas.",
            ],
            [
                "First release: records what you do with the mouse and keyboard and repeats it whenever you want.",
                "Global shortcuts Ctrl+Alt+Shift+R (record) and Ctrl+Alt+Shift+P (play), configurable.",
                "Emergency stop with Pause, Scroll Lock or by holding Esc.",
                "Speed from 0.5× to 100× or your own, and repeat once, N times or forever.",
                "Saves and opens .soctask recordings, and imports .rec files (experimental).",
                "Turns a recording into an .exe that plays it on any PC.",
                "Event editor: delete, trim, simplify mouse moves, change or insert waits.",
            ]),
    ];

    /// <summary>¿Hay que enseñarlas solas al arrancar? Solo al estrenar una version, no en la primera instalacion (sale la guia).</summary>
    public static bool ShouldShow(string lastSeen, string current) =>
        lastSeen.Length > 0 && !string.Equals(lastSeen, current, StringComparison.Ordinal);

    public static IEnumerable<Entry> Latest() => Entries.Take(Shown);
}
