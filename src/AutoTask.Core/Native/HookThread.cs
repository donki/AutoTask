using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SocAutoTask.Native;

/// <summary>
/// Un hilo propio con su bucle de mensajes y ganchos de bajo nivel de raton y/o teclado (RF-03).
/// Windows llama a un gancho LL en el hilo que lo puso, a traves de su bucle de mensajes: por eso
/// nunca se ponen en el hilo de la interfaz (un redibujado lento congelaria el raton de todo el
/// sistema). El gancho solo copia y devuelve.
/// </summary>
public abstract class HookThread : IDisposable
{
    [ThreadStatic]
    private static HookThread? _current;

    private readonly bool _mouse, _keyboard;
    private readonly uint _timerMs;
    private Thread? _thread;
    private uint _threadId;
    private Exception? _startError;

    protected HookThread(bool mouse, bool keyboard, uint timerMs = 0)
    {
        _mouse = mouse;
        _keyboard = keyboard;
        _timerMs = timerMs;
    }

    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Pone los ganchos y vuelve cuando estan puestos (o lanza si Windows no los acepta).</summary>
    public void Start()
    {
        if (_thread is not null)
            throw new InvalidOperationException("Ya esta en marcha.");
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = GetType().Name, Priority = ThreadPriority.Highest };
        _thread.Start();
        ready.Wait();
        if (_startError is not null)
        {
            _thread.Join();
            _thread = null;
            throw _startError;
        }
    }

    /// <summary>Quita los ganchos y espera a que el hilo acabe.</summary>
    public void Stop()
    {
        if (_thread is null)
            return;
        Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, 0, 0);
        _thread.Join(2000);
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private protected virtual void OnMouse(int message, in Win32.MSLLHOOKSTRUCT data) { }

    private protected virtual void OnKeyboard(int message, in Win32.KBDLLHOOKSTRUCT data) { }

    protected virtual void OnTimer() { }

    /// <summary>Para las pruebas: lo que haria el gancho con este evento, sin ganchos de verdad.</summary>
    internal void Simulate(int message, in Win32.MSLLHOOKSTRUCT data) => OnMouse(message, data);

    internal void Simulate(int message, in Win32.KBDLLHOOKSTRUCT data) => OnKeyboard(message, data);

    internal void SimulateTimer() => OnTimer();

    private unsafe void Loop(ManualResetEventSlim ready)
    {
        nint mouseHook = 0, keyboardHook = 0;
        nuint timer = 0;
        _current = this;
        _threadId = Win32.GetCurrentThreadId();
        try
        {
            // Forzar la cola de mensajes del hilo antes de que nadie le escriba.
            Win32.PeekMessage(out _, 0, 0, 0, 0);
            var module = Win32.GetModuleHandle(null);
            if (_mouse)
            {
                mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, &MouseProc, module, 0);
                if (mouseHook == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowsHookEx(WH_MOUSE_LL)");
            }
            if (_keyboard)
            {
                keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, &KeyboardProc, module, 0);
                if (keyboardHook == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowsHookEx(WH_KEYBOARD_LL)");
            }
            if (_timerMs > 0)
                timer = Win32.SetTimer(0, 0, _timerMs, 0);
        }
        catch (Exception ex)
        {
            _startError = ex;
            if (mouseHook != 0) Win32.UnhookWindowsHookEx(mouseHook);
            if (keyboardHook != 0) Win32.UnhookWindowsHookEx(keyboardHook);
            ready.Set();
            return;
        }
        ready.Set();

        try
        {
            while (Win32.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                if (msg.message == Win32.WM_TIMER && msg.hwnd == 0)
                {
                    SafeCall(OnTimer);
                    continue;
                }
                Win32.TranslateMessage(msg);
                Win32.DispatchMessage(msg);
            }
        }
        finally
        {
            if (timer != 0) Win32.KillTimer(0, timer);
            if (mouseHook != 0) Win32.UnhookWindowsHookEx(mouseHook);
            if (keyboardHook != 0) Win32.UnhookWindowsHookEx(keyboardHook);
            _current = null;
        }
    }

    private static void SafeCall(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Una excepcion que saliera de aqui atravesaria codigo nativo: se traga.
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _current is { } self)
        {
            try { self.OnMouse((int)wParam, in *(Win32.MSLLHOOKSTRUCT*)lParam); }
            catch (Exception) { }
        }
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static unsafe nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _current is { } self)
        {
            try { self.OnKeyboard((int)wParam, in *(Win32.KBDLLHOOKSTRUCT*)lParam); }
            catch (Exception) { }
        }
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }
}
