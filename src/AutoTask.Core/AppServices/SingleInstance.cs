using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace SocAutoTask.AppServices;

/// <summary>Que hace una copia nueva al encontrar otra abierta (§8.3).</summary>
public enum InstanceDecision
{
    /// <summary>La otra es de una version anterior: se la cierra y sigue esta.</summary>
    TakeOver,
    /// <summary>Misma version (o mas nueva): se le pide que se enseñe y esta se va.</summary>
    HandOff,
}

/// <summary>
/// Instancia unica (RF-49, constitucion general §8.3): mutex por sesion + tuberia con nombre. La
/// version nueva manda; con la misma version se pasa el turno y se espera el acuse.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const int AckTimeoutMs = 3000;
    public const int QuitTimeoutMs = 5000;
    public const int ConnectTimeoutMs = 5000;

    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly string _version;
    private Mutex? _mutex;
    private bool _owned;
    private CancellationTokenSource? _serverStop;

    public SingleInstance(string version, string? suffix = null)
    {
        _version = version;
        var session = Process.GetCurrentProcess().SessionId;
        _mutexName = $@"Local\sOCAutoTask.Instance{suffix}";
        _pipeName = $"sOCAutoTask.{Environment.UserName}.{session}{suffix}";
    }

    /// <summary>Otra copia pide que esta se enseñe (con el fichero a abrir, o vacio). Llega desde otro hilo.</summary>
    public event Action<string>? ShowRequested;

    /// <summary>Una version mas nueva pide que esta se cierre. Llega desde otro hilo; hay que salir.</summary>
    public event Action? QuitRequested;

    public static InstanceDecision Decide(string mine, string theirs) =>
        TryParse(mine, out var a) && TryParse(theirs, out var b) && b < a ? InstanceDecision.TakeOver : InstanceDecision.HandOff;

    private static bool TryParse(string text, out Version version) => System.Version.TryParse(text.Trim(), out version!);

    /// <summary>
    /// Al arrancar. Devuelve verdadero si esta copia sigue (es la unica, o ha cerrado una vieja, o
    /// la otra no contesto); falso si ha pasado el turno a la que ya estaba abierta.
    /// </summary>
    public bool Claim(string fileToOpen)
    {
        _mutex = new Mutex(false, _mutexName);
        if (TryTakeMutex(0))
        {
            StartServer();
            return true;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            pipe.Connect(ConnectTimeoutMs);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine($"HELLO {_version}");
            var answer = ReadLine(reader, AckTimeoutMs)?.Split(' ');
            if (answer is not ["VERSION", var theirs, var pidText])
                return StartAnyway();

            if (Decide(_version, theirs) == InstanceDecision.TakeOver)
            {
                writer.WriteLine("QUIT");
                if (int.TryParse(pidText, out var pid))
                    WaitOrKill(pid);
                if (TryTakeMutex(QuitTimeoutMs))
                {
                    StartServer();
                    return true;
                }
                return StartAnyway();
            }

            writer.WriteLine($"SHOW {fileToOpen}");
            return ReadLine(reader, AckTimeoutMs) != "ACK" && StartAnyway();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // La otra copia no contesta (colgada o sin ventana): se arranca igual (§8.3).
            AppLog.Write($"instancia unica: la otra copia no contesta ({ex.Message}); se arranca igual");
            return StartAnyway();
        }
    }

    private bool StartAnyway()
    {
        if (TryTakeMutex(0))
            StartServer();
        return true;
    }

    private bool TryTakeMutex(int timeoutMs)
    {
        try
        {
            _owned = _mutex!.WaitOne(timeoutMs);
        }
        catch (AbandonedMutexException)
        {
            _owned = true;   // la otra salio sin soltarlo: es nuestro
        }
        return _owned;
    }

    private static void WaitOrKill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.WaitForExit(QuitTimeoutMs))
            {
                AppLog.Write($"instancia unica: la version anterior (pid {pid}) no se cierra; se termina");
                process.Kill();
                process.WaitForExit(2000);
            }
        }
        catch (ArgumentException)
        {
            // Ya habia salido.
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Write($"instancia unica: no se pudo cerrar la anterior: {ex.Message}");
        }
    }

    private static string? ReadLine(StreamReader reader, int timeoutMs)
    {
        var task = reader.ReadLineAsync();
        return task.Wait(timeoutMs) ? task.Result : null;
    }

    private void StartServer()
    {
        _serverStop = new CancellationTokenSource();
        var token = _serverStop.Token;
        var thread = new Thread(() => Serve(token)) { IsBackground = true, Name = "AutoTask single instance" };
        thread.Start();
    }

    private void Serve(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe.WaitForConnectionAsync(token).GetAwaiter().GetResult();
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                if (ReadLine(reader, AckTimeoutMs) is not { } hello || !hello.StartsWith("HELLO ", StringComparison.Ordinal))
                    continue;
                writer.WriteLine($"VERSION {_version} {Environment.ProcessId}");
                var command = ReadLine(reader, AckTimeoutMs);
                if (command == "QUIT")
                {
                    QuitRequested?.Invoke();
                    return;
                }
                if (command is not null && command.StartsWith("SHOW", StringComparison.Ordinal))
                {
                    ShowRequested?.Invoke(command.Length > 5 ? command[5..] : string.Empty);
                    writer.WriteLine("ACK");
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or AggregateException)
            {
                // Una copia que se fue a mitad de la conversacion, o la tuberia de la version
                // anterior aun sin cerrar: se sigue escuchando tras un respiro.
                if (token.WaitHandle.WaitOne(200))
                    return;
            }
        }
    }

    public void Dispose()
    {
        _serverStop?.Cancel();
        if (_owned)
        {
            try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
            _owned = false;
        }
        _mutex?.Dispose();
        _mutex = null;
    }
}
