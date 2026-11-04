using System.Windows.Forms;

namespace PinchZoom;

/// <summary>
/// Converts between zoom-key names (WinForms <see cref="Keys"/> names, as stored in config.json), virtual-key
/// codes (as the low-level hook reports them) and friendly names for the settings window.
/// </summary>
internal static class KeyNames
{
    // Several Keys members share a code (Capital/CapsLock, Return/Enter, Next/PageDown); pin the one we write.
    private static readonly Dictionary<int, string> ConfigNames = new()
    {
        [0x0D] = "Enter",
        [0x14] = "CapsLock",
        [0x21] = "PageUp",
        [0x22] = "PageDown",
    };

    private static readonly Dictionary<int, string> FriendlyNames = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x13] = "Pause",
        [0x14] = "Caps Lock",
        [0x20] = "Space",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left Arrow",
        [0x26] = "Up Arrow",
        [0x27] = "Right Arrow",
        [0x28] = "Down Arrow",
        [0x2C] = "Print Screen",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x5D] = "Menu",
        [0x90] = "Num Lock",
        [0x91] = "Scroll Lock",
        [0xA0] = "Left Shift",
        [0xA1] = "Right Shift",
        [0xA2] = "Left Ctrl",
        [0xA3] = "Right Ctrl",
        [0xA4] = "Left Alt",
        [0xA5] = "Right Alt",
    };

    /// <summary>The name written to config.json for a virtual-key code.</summary>
    public static string ToConfigName(int vk) =>
        ConfigNames.TryGetValue(vk, out var name) ? name : ((Keys)vk).ToString();

    /// <summary>A readable name for the settings window, such as "Scroll Lock" or "Right Ctrl".</summary>
    public static string ToDisplayName(int vk)
    {
        if (FriendlyNames.TryGetValue(vk, out var name)) return name;
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();         // D0-D9
        if (vk is >= 0x60 and <= 0x69) return $"Num {vk - 0x60}";            // NumPad0-9
        return ToConfigName(vk);
    }

    /// <summary>Why this key cannot be the zoom key, or null if it can.</summary>
    public static string? Check(int vk)
    {
        if (vk is <= 0 or > 0xFE or 0x01 or 0x02 or 0x04 or 0x05 or 0x06)
            return "This key cannot be used as the zoom key.";

        if (vk is 0x10 or 0x11 or 0x12)
            return "Use the left or right key (for example RControlKey), not the generic one.";

        if (vk is 0x5B or 0x5C)
            return "The Windows key opens the Start menu when released; pick another key.";

        return null;
    }

    /// <summary>Resolves a config.json key name to a usable virtual-key code.</summary>
    public static bool TryResolve(string name, out int vk, out string problem)
    {
        vk = 0;
        problem = $"Unknown key \"{name}\".";

        // Enum.TryParse also accepts numbers and comma-joined flags; allow only a single key name.
        if (name.Length == 0 || char.IsDigit(name[0]) || name[0] == '-' || name.Contains(','))
            return false;

        if (!Enum.TryParse<Keys>(name, ignoreCase: true, out var key))
            return false;

        var code = (int)key;
        if (Check(code) is { } reason)
        {
            problem = $"\"{name}\": {reason}";
            return false;
        }

        vk = code;
        return true;
    }
}
