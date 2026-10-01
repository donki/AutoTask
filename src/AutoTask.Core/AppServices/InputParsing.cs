using System.Globalization;
using SocAutoTask.Model;

namespace SocAutoTask.AppServices;

/// <summary>
/// Lo que se escribe en Ajustes, validado (§6.8: nunca se descarta en silencio; si no vale, se
/// dice por que). Admite coma y punto decimal, se escriba en el idioma que se escriba.
/// </summary>
public static class InputParsing
{
    public static bool TryNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.Trim().Replace('×', ' ').Replace('x', ' ').Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <summary>
    /// Opciones de reproduccion a partir de la interfaz. <paramref name="speed"/> es la del
    /// desplegable o null si es «personalizada» (entonces cuenta <paramref name="customSpeed"/>).
    /// Devuelve la clave de Loc del error, o null si todo vale.
    /// </summary>
    public static string? TryPlayback(double? speed, string? customSpeed, RepeatMode repeat, string? times, string? pauseSeconds, out PlaybackOptions options)
    {
        options = new PlaybackOptions();
        double s;
        if (speed is { } preset)
            s = preset;
        else if (!TryNumber(customSpeed, out s) || s < PlaybackOptions.MinSpeed || s > PlaybackOptions.MaxSpeed)
            return "InvalidSpeed";

        var n = 1;
        if (repeat == RepeatMode.Times && (!int.TryParse(times?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1 || n > PlaybackOptions.MaxTimes))
            return "InvalidTimes";
        if (repeat != RepeatMode.Times && int.TryParse(times?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var keep) && keep is >= 1 and <= PlaybackOptions.MaxTimes)
            n = keep;   // se recuerda el numero aunque ahora no se use

        double pause = 0;
        if (!string.IsNullOrWhiteSpace(pauseSeconds) && (!TryNumber(pauseSeconds, out pause) || pause < 0 || pause > PlaybackOptions.MaxPauseMs / 1000.0))
            return "InvalidPause";

        options = new PlaybackOptions { Speed = s, Repeat = repeat, Times = n, PauseBetweenMs = (int)Math.Round(pause * 1000) };
        return null;
    }

    /// <summary>Segundos enteros dentro de un rango (cuenta atras).</summary>
    public static bool TrySeconds(string? text, int min, int max, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return min == 0;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
    }

    /// <summary>Segundos con decimales a milisegundos (Esc mantenida: 0,2 a 10 s).</summary>
    public static bool TrySecondsToMs(string? text, double min, double max, out int ms)
    {
        ms = 0;
        if (!TryNumber(text, out var s) || s < min || s > max)
            return false;
        ms = (int)Math.Round(s * 1000);
        return true;
    }

    /// <summary>Un numero para enseñarlo en una casilla, sin ceros de mas.</summary>
    public static string Show(double value, CultureInfo culture) => value.ToString("0.###", culture);
}
