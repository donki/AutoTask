namespace SocAutoTask.Input;

/// <summary>Teclas de la parada de emergencia que estan activas (RF-13).</summary>
[Flags]
public enum EmergencyKeys
{
    None = 0,
    Pause = 1,
    ScrollLock = 2,
    EscapeHold = 4,
    All = Pause | ScrollLock | EscapeHold,
}

/// <summary>
/// Decide cuando parar por emergencia, a partir de las teclas pulsadas de verdad: Pausa o Bloq
/// Despl al pulsarlas, Esc tras mantenerla el tiempo elegido. Lo inyectado no cuenta (la propia
/// reproduccion puede llevar un Esc). Pura: el gancho le pasa los eventos y un reloj.
/// </summary>
public sealed class EmergencyStopLogic(EmergencyKeys keys, int escapeHoldMs)
{
    public const int DefaultEscapeHoldMs = 1000;

    private double? _escapeSince;

    public EmergencyKeys Keys { get; } = keys;

    public int EscapeHoldMs { get; } = Math.Clamp(escapeHoldMs, 200, 10_000);

    /// <summary>Una tecla. Devuelve verdadero si hay que parar.</summary>
    public bool OnKey(ushort vk, bool down, bool injected, double nowMs)
    {
        if (injected)
            return false;
        switch (vk)
        {
            case KeyNames.Pause when down && Keys.HasFlag(EmergencyKeys.Pause):
                return true;
            case KeyNames.ScrollLock when down && Keys.HasFlag(EmergencyKeys.ScrollLock):
                return true;
            case KeyNames.Escape when Keys.HasFlag(EmergencyKeys.EscapeHold):
                if (!down)
                {
                    _escapeSince = null;
                    return false;
                }
                _escapeSince ??= nowMs;
                return nowMs - _escapeSince.Value >= EscapeHoldMs;
            default:
                return false;
        }
    }

    /// <summary>Se llama de vez en cuando (sin teclas nuevas): Esc lleva pulsada el tiempo suficiente.</summary>
    public bool OnTick(double nowMs) => _escapeSince is { } since && nowMs - since >= EscapeHoldMs;
}

/// <summary>
/// Coordenadas de pantalla para SendInput (ARQUITECTURA §7): Windows recibe 0..65535 sobre el
/// escritorio virtual y lo devuelve a pixel como <c>izq + n·ancho/65536</c>.
/// </summary>
public static class CoordinateMapper
{
    public static int Normalize(int value, int origin, int size)
    {
        if (size <= 1)
            return 0;
        var offset = Math.Clamp((long)value - origin, 0, size - 1);
        return (int)Math.Min(65535, (offset * 65536 + size - 1) / size);
    }

    /// <summary>Lo que hace Windows con la coordenada normalizada (para probar el redondeo).</summary>
    public static int Denormalize(int normalized, int origin, int size) => origin + (int)((long)normalized * size / 65536);
}
