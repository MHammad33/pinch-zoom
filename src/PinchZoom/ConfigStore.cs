using System.IO;
using PinchZoom.Core;
using PinchZoom.Native;

namespace PinchZoom;

/// <summary>Loaded settings, with the zoom key resolved to its virtual-key code.</summary>
internal sealed record LoadedConfig(PinchZoomConfig Config, int VirtualKey, IReadOnlyList<string> Problems);

/// <summary>
/// Reads, creates and saves <c>%AppData%\PinchZoom\config.json</c>. <see cref="Load"/> never throws: every
/// problem is reported and the affected setting falls back to its default.
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
        Save(PinchZoomConfig.Default);
    }

    /// <summary>Writes the config. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
    public static void Save(PinchZoomConfig config)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(ConfigPath, config.ToJson());
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

        if (!KeyNames.TryResolve(config.Key, out var vk, out var keyProblem))
        {
            problems.Add($"{keyProblem} Using {PinchZoomConfig.DefaultKey}.");
            config = config with { Key = PinchZoomConfig.DefaultKey };
            vk = HotkeyService.VK_CAPITAL;
        }

        return new LoadedConfig(config, vk, problems);
    }
}
