using System.IO;

namespace PinchZoom;

/// <summary>Appends errors to <c>%AppData%\PinchZoom\error.log</c>. Never throws, so it is safe in crash handlers.</summary>
internal static class ErrorLog
{
    public static string LogPath { get; } = Path.Combine(ConfigStore.Folder, "error.log");

    public static void Write(string context, object? error)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.Folder);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never turn a handled failure into a crash.
        }
    }
}
