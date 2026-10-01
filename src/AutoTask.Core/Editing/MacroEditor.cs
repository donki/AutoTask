using SocAutoTask.Model;

namespace SocAutoTask.Editing;

/// <summary>
/// Operaciones del editor (RF-35..39). Todas devuelven una lista nueva y no tocan la de entrada.
/// Al quitar un evento, su espera pasa al siguiente que queda: lo de despues no se mueve en el
/// tiempo (salvo al recortar, que es justo lo que se pide).
/// </summary>
public static class MacroEditor
{
    /// <summary>Borra los eventos de esas posiciones (RF-35).</summary>
    public static List<MacroEvent> Delete(IReadOnlyList<MacroEvent> events, IEnumerable<int> indices)
    {
        var remove = new HashSet<int>(indices);
        return Keep(events, i => !remove.Contains(i));
    }

    /// <summary>Quita todo lo anterior a <paramref name="first"/>; ese pasa a empezar sin espera (RF-36).</summary>
    public static List<MacroEvent> TrimStart(IReadOnlyList<MacroEvent> events, int first)
    {
        first = Math.Clamp(first, 0, events.Count);
        var result = new List<MacroEvent>(events.Count - first);
        for (var i = first; i < events.Count; i++)
            result.Add(events[i]);
        if (result.Count > 0)
            result[0] = result[0] with { DelayMs = 0 };
        return result;
    }

    /// <summary>Quita todo lo posterior a <paramref name="last"/> (RF-36).</summary>
    public static List<MacroEvent> TrimEnd(IReadOnlyList<MacroEvent> events, int last)
    {
        last = Math.Clamp(last, -1, events.Count - 1);
        var result = new List<MacroEvent>(last + 1);
        for (var i = 0; i <= last; i++)
            result.Add(events[i]);
        return result;
    }

    /// <summary>
    /// Simplifica los movimientos del raton (RF-37). En cada tramo seguido de movimientos se queda
    /// siempre el ultimo (la posicion antes del clic o de la tecla que viene detras) y, si
    /// <paramref name="onlyLastBeforeClick"/> es falso, tambien los puntos que se separan de la
    /// linea mas de <paramref name="tolerancePx"/> pixeles (Ramer-Douglas-Peucker). El tiempo total
    /// no cambia.
    /// </summary>
    public static List<MacroEvent> SimplifyMoves(IReadOnlyList<MacroEvent> events, double tolerancePx = 3, bool onlyLastBeforeClick = false)
    {
        var keep = new bool[events.Count];
        var i = 0;
        while (i < events.Count)
        {
            if (events[i].Kind != EventKind.MouseMove)
            {
                keep[i] = true;
                i++;
                continue;
            }
            var start = i;
            while (i < events.Count && events[i].Kind == EventKind.MouseMove)
                i++;
            var end = i - 1;   // tramo [start, end]
            keep[end] = true;
            if (!onlyLastBeforeClick)
            {
                keep[start] = true;
                MarkDouglasPeucker(events, start, end, Math.Max(0, tolerancePx), keep);
            }
        }
        return Keep(events, k => keep[k]);
    }

    private static void MarkDouglasPeucker(IReadOnlyList<MacroEvent> events, int first, int last, double tolerance, bool[] keep)
    {
        // Iterativo con pila: un tramo de cientos de miles de puntos no puede reventar la recursion.
        var stack = new Stack<(int A, int B)>();
        stack.Push((first, last));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b <= a + 1)
                continue;
            var farthest = -1;
            var max = tolerance;
            for (var k = a + 1; k < b; k++)
            {
                var d = DistanceToSegment(events[k], events[a], events[b]);
                if (d > max)
                {
                    max = d;
                    farthest = k;
                }
            }
            if (farthest < 0)
                continue;
            keep[farthest] = true;
            stack.Push((a, farthest));
            stack.Push((farthest, b));
        }
    }

    internal static double DistanceToSegment(MacroEvent p, MacroEvent a, MacroEvent b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        var lengthSq = dx * dx + dy * dy;
        if (lengthSq == 0)
            return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0, 1);
        double px = a.X + t * dx - p.X, py = a.Y + t * dy - p.Y;
        return Math.Sqrt(px * px + py * py);
    }

    /// <summary>Pone esa espera a los eventos elegidos (RF-38).</summary>
    public static List<MacroEvent> SetDelay(IReadOnlyList<MacroEvent> events, IEnumerable<int> indices, int delayMs)
    {
        var result = new List<MacroEvent>(events);
        var ms = Math.Max(0, delayMs);
        foreach (var i in indices)
            if (i >= 0 && i < result.Count)
                result[i] = result[i] with { DelayMs = ms };
        return result;
    }

    /// <summary>Multiplica las esperas de los elegidos (o de todos si no se elige ninguno) (RF-38).</summary>
    public static List<MacroEvent> ScaleDelays(IReadOnlyList<MacroEvent> events, IReadOnlyCollection<int>? indices, double factor)
    {
        if (!double.IsFinite(factor) || factor < 0)
            throw new ArgumentOutOfRangeException(nameof(factor));
        var result = new List<MacroEvent>(events);
        IEnumerable<int> targets = indices is { Count: > 0 } ? indices : Enumerable.Range(0, result.Count);
        foreach (var i in targets)
            if (i >= 0 && i < result.Count)
                result[i] = result[i] with { DelayMs = (int)Math.Min(int.MaxValue, Math.Round(result[i].DelayMs * factor)) };
        return result;
    }

    /// <summary>Inserta una espera de <paramref name="ms"/> antes de la posicion dada (o al final) (RF-39).</summary>
    public static List<MacroEvent> InsertWait(IReadOnlyList<MacroEvent> events, int index, int ms)
    {
        var result = new List<MacroEvent>(events);
        result.Insert(Math.Clamp(index, 0, result.Count), MacroEvent.WaitFor(Math.Max(0, ms)));
        return result;
    }

    /// <summary>Se queda con los que cumplen la condicion y pasa la espera de los quitados al siguiente que queda.</summary>
    internal static List<MacroEvent> Keep(IReadOnlyList<MacroEvent> events, Func<int, bool> keep)
    {
        var result = new List<MacroEvent>(events.Count);
        long carried = 0;
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (!keep(i))
            {
                carried += e.DelayMs;
                continue;
            }
            if (carried > 0)
            {
                e = e with { DelayMs = (int)Math.Min(int.MaxValue, e.DelayMs + carried) };
                carried = 0;
            }
            result.Add(e);
        }
        return result;
    }
}

/// <summary>Una sesion del editor: la lista de trabajo y los pasos para deshacer (RF-40).</summary>
public sealed class EditorSession(IEnumerable<MacroEvent> events)
{
    private readonly Stack<List<MacroEvent>> _undo = new();

    /// <summary>Pasos de deshacer que se guardan como mucho (cada uno es una copia de la lista).</summary>
    public const int MaxUndo = 50;

    public List<MacroEvent> Events { get; private set; } = [.. events];

    public bool CanUndo => _undo.Count > 0;

    public bool Changed { get; private set; }

    public void Apply(Func<List<MacroEvent>, List<MacroEvent>> operation)
    {
        var next = operation(Events);
        _undo.Push(Events);
        if (_undo.Count > MaxUndo)
        {
            // La pila no deja quitar por abajo: se rehace sin el mas viejo.
            var kept = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            foreach (var step in kept)
                _undo.Push(step);
        }
        Events = next;
        Changed = true;
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        Events = _undo.Pop();
        Changed = _undo.Count > 0;
    }
}
