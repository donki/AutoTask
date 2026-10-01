using SocAutoTask.Model;

namespace SocAutoTask.Editing;

/// <summary>
/// Deja limpia una grabacion recien hecha (RF-05): quita las sueltas de teclas y botones que ya
/// estaban pulsados al empezar (la del atajo o del clic que puso a grabar) y las pulsaciones que
/// siguen sin soltar al parar (el atajo que paro). La espera de lo quitado pasa al siguiente.
/// </summary>
public static class RecordingCleaner
{
    public static List<MacroEvent> Clean(IReadOnlyList<MacroEvent> events)
    {
        var keep = new bool[events.Count];
        // Por tecla (vk) y por boton: las pulsaciones desde que se pulso, que se quitaran si no se suelta.
        var keyDowns = new Dictionary<ushort, List<int>>();
        var buttonDowns = new Dictionary<MouseButton, List<int>>();

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            switch (e.Kind)
            {
                case EventKind.KeyDown:
                    Down(keyDowns, e.VirtualKey, i);
                    break;
                case EventKind.KeyUp:
                    keep[i] = Up(keyDowns, e.VirtualKey, keep);
                    break;
                case EventKind.MouseDown:
                    Down(buttonDowns, e.Button, i);
                    break;
                case EventKind.MouseUp:
                    keep[i] = Up(buttonDowns, e.Button, keep);
                    break;
                default:
                    keep[i] = true;
                    break;
            }
        }
        // Lo que queda en los diccionarios no se solto: fuera (keep sigue en falso).
        var result = MacroEditor.Keep(events, k => keep[k]);
        if (result.Count > 0)
            result[0] = result[0] with { DelayMs = 0 };
        return result;
    }

    private static void Down<T>(Dictionary<T, List<int>> pressed, T key, int index) where T : notnull
    {
        if (!pressed.TryGetValue(key, out var list))
            pressed[key] = list = [];
        list.Add(index);
    }

    /// <summary>Suelta: si habia pulsacion se confirman sus indices y se guarda la suelta; si no, la suelta sobra.</summary>
    private static bool Up<T>(Dictionary<T, List<int>> pressed, T key, bool[] keep) where T : notnull
    {
        if (!pressed.Remove(key, out var downs))
            return false;
        foreach (var d in downs)
            keep[d] = true;
        return true;
    }
}
