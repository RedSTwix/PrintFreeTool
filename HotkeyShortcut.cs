using System.Windows.Input;

namespace PrintFreeTool;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8
}

public sealed record HotkeyShortcut(int VirtualKey, HotkeyModifiers Modifiers)
{
    public static HotkeyShortcut Default { get; } = new(0x53, HotkeyModifiers.Windows | HotkeyModifiers.Shift);
    public static HotkeyShortcut EditorDefault { get; } = new(0x45, HotkeyModifiers.Windows | HotkeyModifiers.Shift);
    public static HotkeyShortcut ActiveWindowDefault { get; } = new(0x41, HotkeyModifiers.Windows | HotkeyModifiers.Shift);

    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
            parts.Add(GetKeyName(VirtualKey));
            return string.Join("  +  ", parts);
        }
    }

    public bool IsValid => VirtualKey is > 0 and <= 255 && Modifiers != HotkeyModifiers.None && !IsModifierKey(KeyInterop.KeyFromVirtualKey(VirtualKey));

    public static HotkeyShortcut? FromPressedKey(Key key)
    {
        if (IsModifierKey(key))
        {
            return null;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        ModifierKeys wpfModifiers = Keyboard.Modifiers;
        if (wpfModifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (wpfModifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (wpfModifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= HotkeyModifiers.Windows;

        if (modifiers == HotkeyModifiers.None)
        {
            return null;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var shortcut = new HotkeyShortcut(virtualKey, modifiers);
        return shortcut.IsValid ? shortcut : null;
    }

    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;

    private static string GetKeyName(int virtualKey)
    {
        Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        return key switch
        {
            Key.Space => "Espaço",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            _ => key.ToString()
        };
    }
}
