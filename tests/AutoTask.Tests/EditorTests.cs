using SocAutoTask.Editing;
using SocAutoTask.Model;

namespace SocAutoTask.Tests;

/// <summary>Editor (RF-34..40, CA-08) y limpieza tras grabar (RF-05, CA-03).</summary>
public sealed class EditorTests
{
    private static long Total(IEnumerable<MacroEvent> events) => events.Sum(e => (long)e.DelayMs);

    private static List<MacroEvent> Line(int count, int delay = 10) =>
        Enumerable.Range(0, count).Select(i => MacroEvent.Move(i * 10, i * 10, i == 0 ? 0 : delay)).ToList();

    [Fact]
    public void Borrar_PasaLaEsperaAlSiguiente_ElTiempoNoCambia()
    {
        var events = Line(5);
        var result = MacroEditor.Delete(events, [1, 2]);
        Assert.Equal(3, result.Count);
        Assert.Equal(30, result[1].DelayMs);
        Assert.Equal(Total(events), Total(result));
    }

    [Fact]
    public void Borrar_ElUltimo_PierdeSuEspera()
    {
        var result = MacroEditor.Delete(Line(3), [2]);
        Assert.Equal(2, result.Count);
        Assert.Equal(10, Total(result));
    }

    [Fact]
    public void Borrar_IndicesFueraDeRango_NoHaceNada()
    {
        var events = Line(3);
        Assert.Equal(events, MacroEditor.Delete(events, [-1, 7]));
    }

    [Fact]
    public void RecortarInicio_EmpiezaSinEspera()
    {
        var result = MacroEditor.TrimStart(Line(5), 2);
        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[0].DelayMs);
        Assert.Equal(20, result[0].X);
        Assert.Empty(MacroEditor.TrimStart(Line(3), 99));
        Assert.Equal(3, MacroEditor.TrimStart(Line(3), -5).Count);
    }

    [Fact]
    public void RecortarFinal_QuitaLoPosterior()
    {
        var result = MacroEditor.TrimEnd(Line(5), 1);
        Assert.Equal(2, result.Count);
        Assert.Equal(10, result[^1].X);
        Assert.Empty(MacroEditor.TrimEnd(Line(5), -1));
        Assert.Equal(5, MacroEditor.TrimEnd(Line(5), 100).Count);
    }

    [Fact]
    public void Simplificar_LineaRecta_DejaLosExtremos_YElTiempo()
    {
        var events = Line(100);
        events.Add(MacroEvent.Down(MouseButton.Left, 990, 990, 5));
        var result = MacroEditor.SimplifyMoves(events, 3);
        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[0].X);
        Assert.Equal(990, result[1].X);   // el ultimo movimiento antes del clic
        Assert.Equal(EventKind.MouseDown, result[2].Kind);
        Assert.Equal(Total(events), Total(result));
    }

    [Fact]
    public void Simplificar_ConservaLasEsquinas()
    {
        // Una L: de (0,0) a (100,0) y de ahi a (100,100). La esquina se queda.
        var events = new List<MacroEvent>();
        for (var x = 0; x <= 100; x += 10) events.Add(MacroEvent.Move(x, 0, 8));
        for (var y = 10; y <= 100; y += 10) events.Add(MacroEvent.Move(100, y, 8));
        var result = MacroEditor.SimplifyMoves(events, 3);
        Assert.Equal(3, result.Count);
        Assert.Contains(result, e => e.X == 100 && e.Y == 0);
        Assert.Equal(Total(events), Total(result));
    }

    [Fact]
    public void Simplificar_ToleranciaCero_SoloQuitaLoAlineado()
    {
        var events = new List<MacroEvent> { MacroEvent.Move(0, 0), MacroEvent.Move(5, 0, 1), MacroEvent.Move(10, 1, 1), MacroEvent.Move(15, 0, 1) };
        Assert.Equal(4, MacroEditor.SimplifyMoves(events, 0).Count);
    }

    [Fact]
    public void Simplificar_SoloElUltimoAntesDeCadaClic()
    {
        var events = Line(10);
        events.Add(MacroEvent.Down(MouseButton.Left, 90, 90, 1));
        events.Add(MacroEvent.Up(MouseButton.Left, 90, 90, 1));
        events.AddRange(Line(5).Select(e => e with { X = e.X + 500 }));
        events.Add(MacroEvent.Key(true, 0x41, delay: 1));
        var result = MacroEditor.SimplifyMoves(events, 3, onlyLastBeforeClick: true);
        Assert.Equal([EventKind.MouseMove, EventKind.MouseDown, EventKind.MouseUp, EventKind.MouseMove, EventKind.KeyDown], result.Select(e => e.Kind));
        Assert.Equal(90, result[0].X);
        Assert.Equal(540, result[3].X);
        Assert.Equal(Total(events), Total(result));
    }

    [Fact]
    public void Simplificar_TramoEnorme_SinDesbordarLaPila()
    {
        var random = new Random(1);
        var events = Enumerable.Range(0, 300_000).Select(i => MacroEvent.Move(random.Next(0, 1920), random.Next(0, 1080), 1)).ToList();
        var result = MacroEditor.SimplifyMoves(events, 3);
        Assert.Equal(Total(events), Total(result));
        Assert.Equal(events[^1], result[^1] with { DelayMs = events[^1].DelayMs });
    }

    [Fact]
    public void DistanciaAlSegmento()
    {
        Assert.Equal(5, MacroEditor.DistanceToSegment(MacroEvent.Move(5, 5), MacroEvent.Move(0, 0), MacroEvent.Move(10, 0)), 6);
        Assert.Equal(5, MacroEditor.DistanceToSegment(MacroEvent.Move(3, 4), MacroEvent.Move(0, 0), MacroEvent.Move(0, 0)), 6);
        Assert.Equal(5, MacroEditor.DistanceToSegment(MacroEvent.Move(13, 4), MacroEvent.Move(0, 0), MacroEvent.Move(10, 0)), 6);
    }

    [Fact]
    public void CambiarEspera_DeLosElegidos()
    {
        var result = MacroEditor.SetDelay(Line(4), [1, 3, 9], 500);
        Assert.Equal([0, 500, 10, 500], result.Select(e => e.DelayMs));
        Assert.Equal(0, MacroEditor.SetDelay(Line(2), [1], -7)[1].DelayMs);
    }

    [Fact]
    public void Escalar_Todas_OLasElegidas()
    {
        Assert.Equal([0, 5, 5, 5], MacroEditor.ScaleDelays(Line(4), null, 0.5).Select(e => e.DelayMs));
        Assert.Equal([0, 20, 10, 10], MacroEditor.ScaleDelays(Line(4), [1], 2).Select(e => e.DelayMs));
        Assert.Throws<ArgumentOutOfRangeException>(() => MacroEditor.ScaleDelays(Line(2), null, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MacroEditor.ScaleDelays(Line(2), null, double.NaN));
        Assert.Equal(int.MaxValue, MacroEditor.ScaleDelays([MacroEvent.Move(0, 0, int.MaxValue)], null, 3)[0].DelayMs);
    }

    [Fact]
    public void InsertarEspera_DondeToca()
    {
        var at1 = MacroEditor.InsertWait(Line(3), 1, 2000);
        Assert.Equal(EventKind.Wait, at1[1].Kind);
        Assert.Equal(2000, at1[1].DelayMs);
        Assert.Equal(EventKind.Wait, MacroEditor.InsertWait(Line(3), 99, 1)[^1].Kind);
        Assert.Equal(0, MacroEditor.InsertWait(Line(3), 0, -5)[0].DelayMs);
    }

    [Fact]
    public void Sesion_AplicarYDeshacer()
    {
        var session = new EditorSession(Line(5));
        Assert.False(session.CanUndo);
        Assert.False(session.Changed);
        session.Apply(l => MacroEditor.Delete(l, [0]));
        session.Apply(l => MacroEditor.Delete(l, [0]));
        Assert.Equal(3, session.Events.Count);
        Assert.True(session.Changed);
        session.Undo();
        Assert.Equal(4, session.Events.Count);
        Assert.True(session.Changed);
        session.Undo();
        Assert.Equal(5, session.Events.Count);
        Assert.False(session.Changed);
        session.Undo();
        Assert.Equal(5, session.Events.Count);
    }

    [Fact]
    public void Sesion_GuardaComoMucho50Pasos()
    {
        var session = new EditorSession(Line(100));
        for (var i = 0; i < 60; i++)
            session.Apply(l => MacroEditor.Delete(l, [0]));
        var undos = 0;
        while (session.CanUndo)
        {
            session.Undo();
            undos++;
        }
        Assert.Equal(EditorSession.MaxUndo, undos);
        Assert.Equal(90, session.Events.Count);   // 100 - 60 borrados + 50 deshechos
    }

    [Fact]
    public void Limpiar_QuitaElAtajoDeParar()
    {
        // CA-03: se graba algo y se para con Ctrl+Alt+Mayus+R: nada del atajo queda.
        List<MacroEvent> raw =
        [
            MacroEvent.Key(true, 0x48, delay: 0), MacroEvent.Key(false, 0x48, delay: 50),   // H
            MacroEvent.Move(5, 5, 20),
            MacroEvent.Key(true, 0xA2, delay: 300),   // Ctrl izq.
            MacroEvent.Key(true, 0xA4, delay: 30),    // Alt izq.
            MacroEvent.Key(true, 0xA0, delay: 30),    // Mayus izq.
            MacroEvent.Key(true, 0xA0, delay: 30),    // repeticion de Mayus
            MacroEvent.Key(true, 0x52, delay: 30),    // R
        ];
        var clean = RecordingCleaner.Clean(raw);
        Assert.Equal(3, clean.Count);
        Assert.DoesNotContain(clean, e => e.IsKey && e.VirtualKey is 0xA2 or 0xA4 or 0xA0 or 0x52);
    }

    [Fact]
    public void Limpiar_QuitaLasSueltasDelAtajoDeEmpezar_YDelClicEnGrabar()
    {
        List<MacroEvent> raw =
        [
            MacroEvent.Key(false, 0x52, delay: 0),     // R soltada (se pulso antes de grabar)
            MacroEvent.Key(false, 0xA0, delay: 10),
            MacroEvent.Up(MouseButton.Left, 1, 1, 10), // suelta del clic en Grabar
            MacroEvent.Move(100, 100, 40),
            MacroEvent.Down(MouseButton.Left, 100, 100, 40),
            MacroEvent.Up(MouseButton.Left, 100, 100, 60),
        ];
        var clean = RecordingCleaner.Clean(raw);
        Assert.Equal([EventKind.MouseMove, EventKind.MouseDown, EventKind.MouseUp], clean.Select(e => e.Kind));
        Assert.Equal(0, clean[0].DelayMs);   // la primera, sin espera
        Assert.Equal(40, clean[1].DelayMs);
    }

    [Fact]
    public void Limpiar_ConservaRepeticionesDeTeclaSoltada_YClicSinSoltar_SeVa()
    {
        List<MacroEvent> raw =
        [
            MacroEvent.Key(true, 0x41), MacroEvent.Key(true, 0x41, delay: 30), MacroEvent.Key(true, 0x41, delay: 30), MacroEvent.Key(false, 0x41, delay: 30),
            MacroEvent.Down(MouseButton.Right, 1, 1, 5),
        ];
        var clean = RecordingCleaner.Clean(raw);
        Assert.Equal(4, clean.Count);
        Assert.All(clean, e => Assert.True(e.IsKey));
    }

    [Fact]
    public void Limpiar_Vacio() => Assert.Empty(RecordingCleaner.Clean([]));
}
