using System.Text.Json;

namespace PinchZoom.Core;

/// <summary>
/// User settings from <c>config.json</c>: the zoom key (by name) and the hold zoom factor.
/// Parsing is lenient: each bad or missing setting falls back to its default and is reported as a problem.
/// Whether <see cref="Key"/> names a real key is checked by the app, since that needs Windows key codes.
/// </summary>
public sealed record PinchZoomConfig(string Key, double Zoom)
{
    public const string DefaultKey = "CapsLock";

    public static PinchZoomConfig Default { get; } = new(DefaultKey, ZoomStateMachine.DefaultTargetZoom);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(new { key = Key, zoom = Zoom }, WriteOptions);

    public static PinchZoomConfig Parse(string json, out IReadOnlyList<string> problems)
    {
        var found = new List<string>();
        problems = found;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            found.Add($"config.json is not valid JSON: {ex.Message}");
            return Default;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                found.Add("config.json must contain a JSON object.");
                return Default;
            }

            var key = DefaultKey;
            var zoom = ZoomStateMachine.DefaultTargetZoom;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "key", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                        key = prop.Value.GetString()!.Trim();
                    else
                        found.Add($"\"key\" must be a key name such as \"{DefaultKey}\".");
                }
                else if (string.Equals(prop.Name, "zoom", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number
                        && prop.Value.TryGetDouble(out var value)
                        && value > 1.0 && value <= ZoomStateMachine.MaxZoom)
                        zoom = value;
                    else
                        found.Add($"\"zoom\" must be a number above 1 and at most {ZoomStateMachine.MaxZoom}.");
                }
                else
                {
                    found.Add($"Unknown setting \"{prop.Name}\".");
                }
            }

            return new PinchZoomConfig(key, zoom);
        }
    }
}
