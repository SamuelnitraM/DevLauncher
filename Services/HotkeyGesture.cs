namespace DevLauncher.Services;

/// <summary>Global shortcut such as « Ctrl+Alt+D » : Windows modifier flags and virtual key code, as RegisterHotKey expects them.</summary>
public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    public const uint AltModifier = 0x1;
    public const uint ControlModifier = 0x2;
    public const uint ShiftModifier = 0x4;
    public const uint WindowsModifier = 0x8;

    /// <summary>
    /// Reads « Modifier+…+Key » : modifiers Ctrl, Alt, Shift, Win ; key A-Z, 0-9, F1-F24 or Space. At least one modifier is required,
    /// so that the shortcut does not steal a plain key from the other applications.
    /// </summary>
    public static bool TryParse(string? gestureText, out HotkeyGesture? hotkeyGesture)
    {
        hotkeyGesture = null;
        if (string.IsNullOrWhiteSpace(gestureText)) return false;
        var gestureParts = gestureText.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (gestureParts.Length < 2) return false;
        uint modifiers = 0;
        foreach (var modifierName in gestureParts[..^1])
        {
            var modifier = modifierName.ToLowerInvariant() switch
            {
                "ctrl" or "control" or "ctl" => ControlModifier,
                "alt" => AltModifier,
                "shift" or "maj" => ShiftModifier,
                "win" or "windows" => WindowsModifier,
                _ => 0u,
            };
            if (modifier == 0) return false;
            modifiers |= modifier;
        }
        var virtualKey = ParseVirtualKey(gestureParts[^1]);
        if (virtualKey is null) return false;
        hotkeyGesture = new HotkeyGesture(modifiers, virtualKey.Value);
        return true;
    }

    private static uint? ParseVirtualKey(string keyName)
    {
        var upperKeyName = keyName.ToUpperInvariant();
        if (upperKeyName.Length == 1 && upperKeyName[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9')) return upperKeyName[0];
        if (upperKeyName is "SPACE" or "ESPACE") return 0x20;
        if (upperKeyName.Length >= 2 && upperKeyName[0] == 'F' && int.TryParse(upperKeyName[1..], out var functionKeyNumber) && functionKeyNumber is >= 1 and <= 24)
            return (uint)(0x70 + functionKeyNumber - 1);
        return null;
    }
}
