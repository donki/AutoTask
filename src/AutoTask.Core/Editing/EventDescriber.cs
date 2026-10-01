using System.Globalization;
using SocAutoTask.Input;
using SocAutoTask.Localization;
using SocAutoTask.Model;

namespace SocAutoTask.Editing;

/// <summary>Textos del editor para cada evento (RF-34), en el idioma de la interfaz.</summary>
public static class EventDescriber
{
    public static string Kind(in MacroEvent e) => Loc.Get("Kind" + e.Kind);

    public static string Detail(in MacroEvent e) => e.Kind switch
    {
        EventKind.MouseMove => Loc.Format("DetailMove", e.X, e.Y),
        EventKind.MouseDown => Loc.Format("DetailButtonDown", Loc.Get("Button" + e.Button), e.X, e.Y),
        EventKind.MouseUp => Loc.Format("DetailButtonUp", Loc.Get("Button" + e.Button), e.X, e.Y),
        EventKind.Wheel or EventKind.HWheel => Loc.Format("DetailWheel", e.Data.ToString("+0;-0;0", CultureInfo.InvariantCulture), e.X, e.Y),
        EventKind.KeyDown or EventKind.KeyUp => KeyNames.Localize(KeyNames.Name(e.VirtualKey)),
        EventKind.Wait => Loc.Format("DetailWait", FormatMs(e.DelayMs)),
        _ => string.Empty,
    };

    /// <summary>Milisegundos legibles: «850 ms», «12,4 s», «3 min 05 s», «1 h 02 min».</summary>
    public static string FormatMs(double ms)
    {
        var culture = Loc.Culture;
        if (ms < 1000)
            return string.Format(culture, "{0:0} ms", ms);
        var t = TimeSpan.FromMilliseconds(ms);
        if (t.TotalSeconds < 60)
            return string.Format(culture, "{0:0.0} s", t.TotalSeconds);
        if (t.TotalHours < 1)
            return string.Format(culture, "{0} min {1:00} s", (int)t.TotalMinutes, t.Seconds);
        return string.Format(culture, "{0} h {1:00} min", (int)t.TotalHours, t.Minutes);
    }

    /// <summary>Reloj para la cuenta atras: «0:42», «12:05», «1:02:03».</summary>
    public static string Clock(double ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, Math.Ceiling(ms / 1000) * 1000));
        return t.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", t.Minutes, t.Seconds);
    }

    /// <summary>La velocidad como se enseña: «0,5×», «100×».</summary>
    public static string Speed(double speed) => speed.ToString("0.##", Loc.Culture) + "×";
}
