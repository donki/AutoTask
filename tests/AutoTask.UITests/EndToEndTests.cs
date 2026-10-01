using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using SocAutoTask.Compile;
using SocAutoTask.Model;
using SocAutoTask.Native;
using SocAutoTask.Playback;

namespace SocAutoTask.UITests;

/// <summary>
/// Prueba de verdad (CA-01, CA-02, CA-07): grabar con los ganchos y reproducir con SendInput contra
/// una ventana de prueba propia (AutoTask.TestTarget). MUEVE EL RATON Y TECLEA: solo con
/// AUTOTASK_E2E=1, en rafagas de pocos segundos, comprobando antes de teclear que la ventana de
/// prueba esta en primer plano, y devolviendo el raton a donde estaba.
/// </summary>
/// <remarks>
/// Lo que «hace el usuario» aqui lo simula la propia prueba con SendInput; por eso el grabador se
/// pone con <see cref="RecorderOptions.IncludeInjected"/> (en uso normal ignora lo inyectado).
/// </remarks>
public sealed class EndToEndTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("AUTOTASK_E2E") == "1";

    private sealed class Target : IDisposable
    {
        // Solo el numero de proceso: FlaUI se queda el objeto Process y lo libera por su cuenta.
        public int Pid { get; }
        public UIA3Automation Automation { get; } = new();
        public FlaUI.Core.AutomationElements.Window Window { get; }
        public TextBox Box { get; }
        public AutomationElement Button { get; }
        public AutomationElement Count { get; }

        public Target(int x, int y)
        {
            var exe = Path.Combine(AtApp.RepoRoot, "tests", "AutoTask.UITests", "AutoTask.TestTarget", "bin", "Debug", "net10.0-windows", "AutoTask.TestTarget.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException("Compila antes AutoTask.TestTarget (dotnet build tests\\AutoTask.UITests\\AutoTask.TestTarget)", exe);
            using (var process = Process.Start(new ProcessStartInfo(exe, $"{x} {y}") { UseShellExecute = false })!)
            {
                Pid = process.Id;
                process.WaitForInputIdle(10_000);
            }
            try
            {
                var app = FlaUI.Core.Application.Attach(Pid);
                Window = Retry.WhileNull(() => app.GetMainWindow(Automation, TimeSpan.FromSeconds(1)), TimeSpan.FromSeconds(15), throwOnTimeout: true).Result!;
                Box = Window.FindFirstDescendant(c => c.ByAutomationId("TargetBox"))!.AsTextBox();
                Button = Window.FindFirstDescendant(c => c.ByAutomationId("CountButton"))!;
                Count = Window.FindFirstDescendant(c => c.ByAutomationId("CountText"))!;
            }
            catch
            {
                // Si algo falla aqui no se llega a Dispose: la ventana de prueba no se puede quedar abierta.
                Dispose();
                throw;
            }
        }

        public int Clicks => int.Parse(Count.Name);

        public (int X, int Y) Center(AutomationElement e)
        {
            var r = e.BoundingRectangle;   // pixeles fisicos: este proceso es per-monitor DPI aware (app.manifest)
            return (r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        public bool IsForeground() => GetWindowThreadProcessId(GetForegroundWindow(), out var pid) != 0 && pid == Pid;

        public void Dispose()
        {
            try
            {
                using var process = Process.GetProcessById(Pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception) { }
            Automation.Dispose();
        }
    }

    /// <summary>Lo que haria una persona: clic en la casilla, escribir «hola», tres clics en el boton.</summary>
    private static void ActAsUser(Target target, IInputSink sink)
    {
        void Pause() => Thread.Sleep(25);
        var (bx, by) = target.Center(target.Box);
        var (cx, cy) = target.Center(target.Button);
        sink.Send(MacroEvent.Move(bx - 30, by));
        Pause();
        sink.Send(MacroEvent.Move(bx, by));
        Pause();
        sink.Send(MacroEvent.Down(MouseButton.Left, bx, by));
        Pause();
        sink.Send(MacroEvent.Up(MouseButton.Left, bx, by));
        Thread.Sleep(150);
        Assert.True(target.IsForeground(), "La ventana de prueba no esta en primer plano: no se teclea (§8.4)");
        foreach (var vk in new ushort[] { 0x48, 0x4F, 0x4C, 0x41 })   // h o l a
        {
            sink.Send(MacroEvent.Key(true, vk));
            Pause();
            sink.Send(MacroEvent.Key(false, vk));
            Pause();
        }
        sink.Send(MacroEvent.Move((bx + cx) / 2, (by + cy) / 2));
        Pause();
        for (var i = 0; i < 3; i++)
        {
            sink.Send(MacroEvent.Move(cx, cy));
            sink.Send(MacroEvent.Down(MouseButton.Left, cx, cy));
            Pause();
            sink.Send(MacroEvent.Up(MouseButton.Left, cx, cy));
            Thread.Sleep(60);
        }
    }

    /// <summary>
    /// Espera a que la persona lleve unos segundos sin tocar el raton ni el teclado (§8.4): si no,
    /// lo suyo se mezclaria con la prueba (y la prueba con lo suyo). Como mucho un minuto.
    /// </summary>
    private static void WaitUserIdle()
    {
        var until = DateTime.UtcNow.AddMinutes(1);
        while (IdleMs() < 4000)
        {
            Assert.True(DateTime.UtcNow < until, "La persona esta usando el PC: no se lanza la prueba E2E");
            Thread.Sleep(500);
        }
    }

    private static uint IdleMs()
    {
        var info = new LastInputInfo { Size = 8 };
        GetLastInputInfo(ref info);
        return unchecked((uint)Environment.TickCount - info.Time);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private static Recording RecordUser(Target target)
    {
        WaitUserIdle();
        using var recorder = new Recorder(new RecorderOptions { IncludeInjected = true });
        recorder.Start();
        Thread.Sleep(100);
        ActAsUser(target, new Win32InputSink());
        Thread.Sleep(100);
        return recorder.StopAndCollect(new PlaybackOptions { Speed = 2 });
    }

    private static void Play(Recording recording)
    {
        var session = new PlaybackSession(recording.Events, recording.Options, new Win32InputSink(), new StopwatchClock(), new SystemKeyboardState());
        Assert.Equal(StopReason.None, session.Run());
    }

    [Fact]
    public void GrabarYReproducir_DeVerdad_EnLaVentanaDePrueba()
    {
        if (!Enabled)
            return;
        var cursor = Screens.Cursor();
        using var target = new Target(240, 240);
        try
        {
            var recording = RecordUser(target);
            File.WriteAllText(Path.Combine(AtApp.ArtifactsFolder, "e2e-grabacion.txt"),
                string.Join(Environment.NewLine, recording.Events.Select(e => $"{e.DelayMs,5} {e.Kind,-10} {e.X},{e.Y} {e.Data:X}")));
            Assert.Equal("hola", target.Box.Text);
            Assert.Equal(3, target.Clicks);

            // CA-01: lo grabado tiene los clics y las teclas, sin nada de mas.
            Assert.Equal(4, recording.Events.Count(e => e.Kind == EventKind.MouseDown));
            Assert.Equal(4, recording.Events.Count(e => e.Kind == EventKind.KeyDown));
            Assert.Equal(4, recording.Events.Count(e => e.Kind == EventKind.KeyUp));

            WaitUserIdle();
            target.Box.Text = string.Empty;
            Play(recording);
            Thread.Sleep(300);
            AtApp.CaptureWindow(target.Window.Properties.NativeWindowHandle.Value, "e2e-reproducida");
            Assert.Equal("hola", target.Box.Text);
            Assert.Equal(6, target.Clicks);
        }
        finally
        {
            Screens.SetCursor(cursor.X, cursor.Y);
        }
    }

    [Fact]
    public void ClicEnElMonitorDeLaIzquierda_CaeDondeToca()
    {
        // CA-02: coordenadas negativas del escritorio virtual (monitor a la izquierda del principal).
        if (!Enabled || Screens.Virtual().Left >= 0)
            return;
        var cursor = Screens.Cursor();
        var screen = Screens.Virtual();
        using var target = new Target(screen.Left + 300, 300);
        try
        {
            WaitUserIdle();
            var (cx, cy) = target.Center(target.Button);
            Assert.True(cx < 0, $"la ventana de prueba no esta en el monitor de la izquierda ({cx})");
            var sink = new Win32InputSink();
            sink.Send(MacroEvent.Move(cx, cy));
            Thread.Sleep(50);
            Assert.Equal((cx, cy), Screens.Cursor());   // el mismo pixel
            sink.Send(MacroEvent.Down(MouseButton.Left, cx, cy));
            Thread.Sleep(30);
            sink.Send(MacroEvent.Up(MouseButton.Left, cx, cy));
            Assert.True(Retry.WhileFalse(() => target.Clicks == 1, TimeSpan.FromSeconds(5)).Result);
        }
        finally
        {
            Screens.SetCursor(cursor.X, cursor.Y);
        }
    }

    [Fact]
    public void ExeCompilado_ReproduceLaGrabacion()
    {
        // CA-07: el exe generado con el reproductor de verdad repite el texto y los clics.
        if (!Enabled)
            return;
        var player = Path.Combine(AtApp.RepoRoot, "artifacts", "player", "sOCAutoTaskPlayer.exe");
        Assert.True(File.Exists(player), "Compila antes el reproductor: tools\\compilar-reproductor.ps1");
        var cursor = Screens.Cursor();
        using var target = new Target(260, 260);
        var dir = Path.Combine(Path.GetTempPath(), "sOCAutoTask-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var recording = RecordUser(target);
            target.Box.Text = string.Empty;
            WaitUserIdle();
            var exe = Path.Combine(dir, "macro-prueba.exe");
            using (var stream = File.OpenRead(player))
                ExeBuilder.Build(stream, recording, new ExeOptions(), exe);
            using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })!;
            Assert.True(process.WaitForExit(30_000), "el exe compilado no acaba");
            Assert.Equal(0, process.ExitCode);
            Thread.Sleep(300);
            Assert.Equal("hola", target.Box.Text);
            Assert.Equal(6, target.Clicks);
        }
        finally
        {
            Screens.SetCursor(cursor.X, cursor.Y);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
}
