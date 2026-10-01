using System.Diagnostics;
using SocAutoTask.Model;

namespace SocAutoTask.Playback;

/// <summary>A donde van los eventos al reproducir: SendInput de verdad o un doble en las pruebas.</summary>
public interface IInputSink
{
    void Send(in MacroEvent e);
}

/// <summary>Reloj de la reproduccion, en milisegundos. El de verdad usa Stopwatch; las pruebas, uno falso.</summary>
public interface IClock
{
    double NowMs { get; }

    /// <summary>Espera hasta ese instante. Devuelve falso si se pidio parar mientras tanto.</summary>
    bool WaitUntil(double targetMs, PlaybackControl control);

    /// <summary>Resolucion del temporizador del sistema a 1 ms mientras dure (timeBeginPeriod).</summary>
    IDisposable HighResolution();
}

/// <summary>¿Hay algun modificador pulsado de verdad? (RF-15) Lo de verdad mira GetAsyncKeyState.</summary>
public interface IKeyboardState
{
    bool AnyModifierDown();
}

/// <summary>Por que se paro.</summary>
public enum StopReason
{
    None,
    User,
    Hotkey,
    Emergency,
    Closing,
}

/// <summary>Bandera de parada compartida entre la interfaz, el vigilante de emergencia y el reproductor.</summary>
public sealed class PlaybackControl : IDisposable
{
    private readonly ManualResetEvent _stopped = new(false);
    private int _reason;

    public bool IsStopRequested => _reason != 0;

    public StopReason Reason => (StopReason)_reason;

    public WaitHandle Handle => _stopped;

    /// <summary>Pide parar. Solo cuenta la primera razon.</summary>
    public void Stop(StopReason reason = StopReason.User)
    {
        if (Interlocked.CompareExchange(ref _reason, (int)reason, 0) == 0)
            _stopped.Set();
    }

    public void Dispose() => _stopped.Dispose();
}

/// <summary>El reloj de verdad: Stopwatch, esperas que despiertan al parar y espera activa los ultimos 2 ms.</summary>
public sealed class StopwatchClock : IClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public double NowMs => _watch.Elapsed.TotalMilliseconds;

    public bool WaitUntil(double targetMs, PlaybackControl control)
    {
        while (true)
        {
            if (control.IsStopRequested)
                return false;
            var left = targetMs - NowMs;
            if (left <= 0)
                return true;
            if (left > 2.5)
            {
                // Despierta al momento si se pide parar (RF-13 durante esperas largas).
                if (control.Handle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(left - 2, 250))))
                    return false;
            }
            else
            {
                Thread.SpinWait(50);
            }
        }
    }

    public IDisposable HighResolution() => new TimerResolution();

    private sealed class TimerResolution : IDisposable
    {
        private bool _done;

        public TimerResolution() => Native.Win32.timeBeginPeriod(1);

        public void Dispose()
        {
            if (_done)
                return;
            _done = true;
            Native.Win32.timeEndPeriod(1);
        }
    }
}
