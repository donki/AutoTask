using SocAutoTask.Input;
using SocAutoTask.Model;
using SocAutoTask.Native;

namespace SocAutoTask.Tests;

/// <summary>
/// Lo de Windows que se puede probar sin mover el raton ni teclear: poner y quitar los ganchos,
/// pantallas, elevacion. Enviar eventos de verdad (SendInput) solo en la prueba E2E de
/// AutoTask.UITests, con AUTOTASK_E2E=1.
/// </summary>
public sealed class NativeTests
{
    [Fact]
    public void Grabador_PoneYQuitaLosGanchos_SinNadaQueGrabar()
    {
        using var recorder = new Recorder(new RecorderOptions { Keyboard = true, MouseMoves = false });
        recorder.SetIgnoredArea(new ScreenRect(0, 0, 10, 10));
        recorder.Start();
        Assert.True(recorder.IsRunning);
        Assert.Throws<InvalidOperationException>(recorder.Start);
        recorder.SetIgnoredArea(null);
        var recording = recorder.StopAndCollect(new PlaybackOptions { Speed = 3 });
        Assert.False(recorder.IsRunning);
        Assert.Equal(3, recording.Options.Speed);
        Assert.False(recording.Screen.IsEmpty);
        Assert.Equal(Screens.Virtual(), recording.Screen);
        recorder.Stop();   // dos veces no pasa nada
    }

    [Fact]
    public void VigilanteDeEmergencia_ArrancaYPara_SinDispararse()
    {
        var fired = false;
        using (var watcher = new EmergencyStopWatcher(EmergencyKeys.All, 1000, () => fired = true))
        {
            watcher.Start();
            Assert.True(watcher.IsRunning);
            Thread.Sleep(150);   // unas vueltas del temporizador de la Esc mantenida
        }
        Assert.False(fired);
    }

    [Fact]
    public void Pantallas_YCursor()
    {
        var screen = Screens.Virtual();
        Assert.True(screen.Width > 0 && screen.Height > 0);
        var (x, y) = Screens.Cursor();
        Assert.True(screen.Contains(x, y) || (x, y) == (0, 0));
    }

    [Fact]
    public void Elevacion_PreguntasSeguras()
    {
        var elevated = Elevation.IsCurrentProcessElevated();
        Assert.False(Elevation.IsWindowElevated(0));
        // Las pruebas no corren elevadas: nada en primer plano puede bloquearlas «por ser de administrador» si ellas lo son.
        if (elevated)
            Assert.False(Elevation.ForegroundIsElevatedAndWeAreNot());
        else
            _ = Elevation.ForegroundIsElevatedAndWeAreNot();
    }

    [Fact]
    public void Teclado_PreguntaSinFallar() => _ = new SystemKeyboardState().AnyModifierDown();
}
