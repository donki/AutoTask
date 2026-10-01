using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocAutoTask.Input;
using SocAutoTask.Localization;

namespace SocAutoTask.Desktop.Controls;

/// <summary>
/// Casilla para elegir un atajo: se pulsa la combinacion y se queda escrita («Ctrl+Alt+Mayús+R»).
/// Retroceso o Supr sin modificadores la dejan como estaba al abrir. Mientras esta abierta, los
/// atajos globales estan quitados (si no, Windows se tragaria la combinacion).
/// </summary>
public sealed class HotkeyBox : TextBox
{
    private Hotkey _hotkey;
    private Hotkey _original;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        Loc.LanguageChanged += () => Text = _hotkey.Display();
    }

    public Hotkey Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            if (_original == default)
                _original = value;
            Text = value.Display();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = false;   // Tab sigue sirviendo para moverse
            return;
        }
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            Hotkey = _original;
            return;
        }
        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;
        var candidate = new Hotkey(mods, (ushort)KeyInterop.VirtualKeyFromKey(key));
        if (candidate.IsValid)
            Hotkey = candidate;
        else
            Text = candidate.Display() + "  ·  " + Loc.Get("HotkeyNeedsModifier");
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Text = _hotkey.Display();
    }
}
