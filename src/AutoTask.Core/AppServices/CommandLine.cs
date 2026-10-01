namespace SocAutoTask.AppServices;

/// <summary>
/// <c>sOCAutoTask.exe [fichero] [--play] [--wait-pid N]</c>: abre un fichero (asociacion, arrastrar
/// al icono), lo reproduce al abrir, o espera a que salga la copia que se reinicio como administrador.
/// </summary>
public static class CommandLine
{
    public static (string? File, bool Play, int? WaitPid) Parse(IReadOnlyList<string> args)
    {
        string? file = null;
        var play = false;
        int? pid = null;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.Equals("--play", StringComparison.OrdinalIgnoreCase))
                play = true;
            else if (a.Equals("--wait-pid", StringComparison.OrdinalIgnoreCase))
            {
                // Lo siguiente es el numero de proceso (aunque no lo sea: nunca un fichero).
                if (i + 1 < args.Count && int.TryParse(args[i + 1], out var p))
                    pid = p;
                i++;
            }
            else if (!a.StartsWith("--", StringComparison.Ordinal) && file is null && a.Length > 0)
                file = Path.GetFullPath(a);
        }
        return (file, play, pid);
    }
}
