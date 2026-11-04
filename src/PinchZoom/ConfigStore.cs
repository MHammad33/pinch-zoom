using System.IO;
using System.Windows.Forms;
using PinchZoom.Core;
using PinchZoom.Native;

namespace PinchZoom;

/// <summary>Loaded settings, with the zoom key resolved to its virtual-key code.</summary>
internal sealed record LoadedConfig(PinchZoomConfig Config, int VirtualKey, IReadOnlyList<string> Problems);

/// <summary>
/// Reads and creates <c>%AppData%\PinchZoom\config.json</c>. Never throws: every problem is reported and the
/// affected setting falls back to its default.
/// </summary>
internal static class ConfigStore
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinchZoom");

    public static string ConfigPath { get; } = Path.Combine(Folder, "config.json");

    /// <summary>Writes the default config if the file does not exist yet.</summary>
    public static void EnsureExists()
    {
        if (File.Exists(ConfigPath)) return;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(ConfigPath, PinchZoomConfig.Default.ToJson());
    }

    public static LoadedConfig Load()
    {
        var problems = new List<string>();
        var config = PinchZoomConfig.Default;

        try
        {
            EnsureExists();
            config = PinchZoomConfig.Parse(File.ReadAllText(ConfigPath), out var parseProblems);
            problems.AddRange(parseProblems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"Could not read {ConfigPath}: {ex.Message}");
        }

        if (!TryResolveKey(config.Key, out var vk, out var keyProblem))
        {
            problems.Add(keyProblem);
            config = config with { Key = PinchZoomConfig.DefaultKey };
            vk = HotkeyService.VK_CAPITAL;
        }

        return new LoadedConfig(config, vk, problems);
    }

    /// <summary>
    /// Maps a key name (a WinForms <see cref="Keys"/> name such as CapsLock, Scroll, RControlKey or F13) to the
    /// virtual-key code the low-level hook reports.
    /// </summary>
    private static bool TryResolveKey(string name, out int vk, out string problem)
    {
        vk = 0;
        problem = $"Unknown key \"{name}\" in config.json; using {PinchZoomConfig.DefaultKey}.";

        // Enum.TryParse also accepts numbers and comma-joined flags; allow only a single key name.
        if (name.Length == 0 || char.IsDigit(name[0]) || name[0] == '-' || name.Contains(','))
            return false;

        if (!Enum.TryParse<Keys>(name, ignoreCase: true, out var key))
            return false;

        var code = (int)key;
        if (code is <= 0 or > 0xFE or 0x01 or 0x02 or 0x04 or 0x05 or 0x06)
        {
            // Modifier flags, mouse buttons and None never reach a keyboard hook.
            problem = $"\"{name}\" cannot be used as the zoom key; using {PinchZoomConfig.DefaultKey}.";
            return false;
        }

        if (code is 0x10 or 0x11 or 0x12)
        {
            // The low-level hook reports the left or right variant, never the generic modifier.
            problem = $"\"{name}\" is ambiguous; use the left or right key (for example RControlKey). Using {PinchZoomConfig.DefaultKey}.";
            return false;
        }

        vk = code;
        return true;
    }
}
