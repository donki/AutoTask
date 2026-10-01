using System.Text.Json;
using System.Text.Json.Serialization;
using SocAutoTask.Input;
using SocAutoTask.Model;

namespace SocAutoTask.AppServices;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Ajustes de la aplicacion, en <c>settings.json</c> de la carpeta de datos.</summary>
public sealed class AppSettings
{
    public const int MaxRecent = 8;

    /// <summary>"es", "en" o null (el de Windows).</summary>
    public string? Language { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    public string RecordHotkey { get; set; } = Hotkey.DefaultRecord.ToString();

    public string PlayHotkey { get; set; } = Hotkey.DefaultPlay.ToString();

    public EmergencyKeys Emergency { get; set; } = EmergencyKeys.All;

    public int EscapeHoldMs { get; set; } = EmergencyStopLogic.DefaultEscapeHoldMs;

    public int CountdownSeconds { get; set; }

    public bool RecordKeyboard { get; set; } = true;

    public bool RecordMouseMoves { get; set; } = true;

    public bool AlwaysOnTop { get; set; }

    public bool ShowLabels { get; set; } = true;

    public bool TrayOnMinimize { get; set; } = true;

    /// <summary>Velocidad y repeticiones con que empieza una grabacion nueva (las ultimas usadas, RF-21).</summary>
    public PlaybackOptions DefaultPlayback { get; set; } = new();

    public List<string> Recent { get; set; } = [];

    public bool GuideShown { get; set; }

    public string LastSeenVersion { get; set; } = string.Empty;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    [JsonIgnore]
    public Hotkey RecordKey => Hotkey.ParseOr(RecordHotkey, Hotkey.DefaultRecord);

    [JsonIgnore]
    public Hotkey PlayKey => Hotkey.ParseOr(PlayHotkey, Hotkey.DefaultPlay);

    /// <summary>Pone un fichero el primero de recientes (sin repetir, como mucho <see cref="MaxRecent"/>).</summary>
    public void AddRecent(string path)
    {
        Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        if (Recent.Count > MaxRecent)
            Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
    }

    /// <summary>Quita de recientes los que ya no existen (RF-25). Devuelve si ha cambiado algo.</summary>
    public bool PruneRecent(Func<string, bool> exists) => Recent.RemoveAll(p => !exists(p)) > 0;

    /// <summary>Lleva a sus limites lo que venga a mano en el fichero.</summary>
    public void Normalize()
    {
        CountdownSeconds = Math.Clamp(CountdownSeconds, 0, 10);
        EscapeHoldMs = Math.Clamp(EscapeHoldMs, 200, 10_000);
        Emergency &= EmergencyKeys.All;
        DefaultPlayback = (DefaultPlayback ?? new PlaybackOptions()).Normalized();
        Recent ??= [];
        if (Recent.Count > MaxRecent)
            Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
        if (!Hotkey.TryParse(RecordHotkey, out _))
            RecordHotkey = Hotkey.DefaultRecord.ToString();
        if (!Hotkey.TryParse(PlayHotkey, out _) || RecordKey == PlayKey)
            PlayHotkey = Hotkey.DefaultPlay.ToString();
        if (RecordKey == PlayKey)
            RecordHotkey = Hotkey.DefaultRecord.ToString();
        if (Language is not ("es" or "en"))
            Language = null;
    }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.AppSettings) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Write($"ajustes ilegibles, se empieza con los de fabrica: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
        File.Move(temp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
