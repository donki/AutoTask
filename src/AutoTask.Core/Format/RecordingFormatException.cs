namespace SocAutoTask.Format;

/// <summary>Por que no se ha podido leer un fichero de grabacion. Cada una tiene su texto en Loc (§6.9).</summary>
public enum FormatProblem
{
    /// <summary>No es un .soctask (otra firma).</summary>
    NotARecording,
    /// <summary>Hecho con una version mas nueva del formato.</summary>
    NewerVersion,
    /// <summary>Se acaba antes de tiempo (copia a medias).</summary>
    Truncated,
    /// <summary>CRC, recuento o valores imposibles: modificado o dañado.</summary>
    Corrupt,
    /// <summary>No tiene ningun evento.</summary>
    Empty,
    /// <summary>Un .rec que no tiene la forma esperada.</summary>
    NotARec,
}

/// <summary>Fichero de grabacion que no se puede usar. <see cref="Exception.Message"/> es tecnico (va al registro).</summary>
public sealed class RecordingFormatException(FormatProblem problem, string detail) : Exception(detail)
{
    public FormatProblem Problem { get; } = problem;

    /// <summary>Clave de Loc con el texto para el usuario: la razon y que hacer.</summary>
    public string LocKey => "Format" + Problem;
}
