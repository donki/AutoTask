using System.Globalization;

namespace SocAutoTask.Input;

/// <summary>Modificadores de un atajo. Los valores son los de RegisterHotKey (MOD_*).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Un atajo global: modificadores + una tecla. Se guarda como texto invariable
/// («Ctrl+Alt+Shift+R») y se enseña traducido («Ctrl+Alt+Mayús+R»).
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, ushort Key)
{
    public static readonly Hotkey DefaultRecord = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x52);   // R
    public static readonly Hotkey DefaultPlay = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x50);     // P

    /// <summary>Un atajo valido tiene al menos un modificador y una tecla que no es un modificador.</summary>
    public bool IsValid => Modifiers != HotkeyModifiers.None && Key != 0 && !KeyNames.IsModifier(Key);

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyNames.Name(Key));
        return string.Join('+', parts);
    }

    /// <summary>El atajo como lo lee la persona, en su idioma.</summary>
    public string Display() => string.Join('+', ToString().Split('+').Select(KeyNames.Localize));

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var mods = HotkeyModifiers.None;
        ushort key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; continue;
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "shift" or "mayús" or "mayus": mods |= HotkeyModifiers.Shift; continue;
                case "win" or "windows": mods |= HotkeyModifiers.Win; continue;
            }
            if (key != 0 || !KeyNames.TryParse(raw, out key))
                return false;
        }
        hotkey = new Hotkey(mods, key);
        return hotkey.IsValid;
    }

    public static Hotkey ParseOr(string? text, Hotkey fallback) => TryParse(text, out var h) ? h : fallback;
}

/// <summary>Nombres de teclas (codigos virtuales) para guardar y enseñar.</summary>
public static class KeyNames
{
    public const ushort Shift = 0x10, Control = 0x11, Menu = 0x12, Pause = 0x13, Escape = 0x1B, ScrollLock = 0x91;
    public const ushort LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, LMenu = 0xA4, RMenu = 0xA5, LWin = 0x5B, RWin = 0x5C;

    private static readonly Dictionary<ushort, string> Names = Build();
    private static readonly Dictionary<string, ushort> ByName = Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    public static bool IsModifier(ushort vk) =>
        vk is Shift or Control or Menu or LShift or RShift or LControl or RControl or LMenu or RMenu or LWin or RWin;

    public static string Name(ushort vk) => Names.TryGetValue(vk, out var n) ? n : "VK" + vk.ToString("X2", CultureInfo.InvariantCulture);

    public static bool TryParse(string text, out ushort vk)
    {
        if (ByName.TryGetValue(text, out vk))
            return true;
        if (text.StartsWith("VK", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk) && vk is > 0 and < 0xFF)
            return true;
        vk = 0;
        return false;
    }

    /// <summary>Nombre en el idioma de la interfaz: Loc tiene «Key_Shift» = «Mayús», etc.; si no, el invariable.</summary>
    public static string Localize(string name)
    {
        var text = Localization.Loc.Get("Key_" + name);
        return text.Length > 0 ? text : name;
    }

    private static Dictionary<ushort, string> Build()
    {
        var d = new Dictionary<ushort, string>
        {
            [0x08] = "Back", [0x09] = "Tab", [0x0D] = "Enter", [Shift] = "Shift", [Control] = "Ctrl", [Menu] = "Alt",
            [Pause] = "Pause", [0x14] = "CapsLock", [Escape] = "Esc", [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown",
            [0x23] = "End", [0x24] = "Home", [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete", [LWin] = "LWin", [RWin] = "RWin", [0x5D] = "Apps",
            [0x6A] = "Multiply", [0x6B] = "Add", [0x6D] = "Subtract", [0x6E] = "Decimal", [0x6F] = "Divide",
            [0x90] = "NumLock", [ScrollLock] = "ScrollLock",
            [LShift] = "LShift", [RShift] = "RShift", [LControl] = "LCtrl", [RControl] = "RCtrl", [LMenu] = "LAlt", [RMenu] = "RAlt",
            [0xBA] = "Oem1", [0xBB] = "OemPlus", [0xBC] = "OemComma", [0xBD] = "OemMinus", [0xBE] = "OemPeriod", [0xBF] = "Oem2",
            [0xC0] = "Oem3", [0xDB] = "Oem4", [0xDC] = "Oem5", [0xDD] = "Oem6", [0xDE] = "Oem7", [0xE2] = "Oem102",
        };
        for (ushort c = 'A'; c <= 'Z'; c++)
            d[c] = ((char)c).ToString();
        for (ushort c = '0'; c <= '9'; c++)
            d[c] = ((char)c).ToString();
        for (ushort i = 0; i < 10; i++)
            d[(ushort)(0x60 + i)] = "NumPad" + i.ToString(CultureInfo.InvariantCulture);
        for (ushort i = 1; i <= 24; i++)
            d[(ushort)(0x6F + i)] = "F" + i.ToString(CultureInfo.InvariantCulture);
        return d;
    }
}
