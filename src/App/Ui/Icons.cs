// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;

using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>
/// Vector icons on a 24x24 grid, loaded from embedded JSON.
/// icons.json: Fluent UI System Icons (MIT). logos.json: LobeHub Icons (MIT) and Simple Icons (CC0); brand logos are trademarks of their owners.
/// </summary>
internal static class Icons
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    public record Part(string D, double? O);
    public record Shape(Geometry Data, double Opacity);

    private static readonly Dictionary<string, Shape[]> _ui = Load("icons.json"), _logos = Load("logos.json");

    /// <summary>UI icon by name (file name without _24_regular). Throws on unknown names: a typo is a bug.</summary>
    public static Shape[] Get(string name) => _ui[name];

    public static Shape[]? Logo(string? key) => key != null && _logos.TryGetValue(key, out var s) ? s : null;

    public static string ForCategory(string category) => category switch
    {
        "agent" => "bot",
        "orchestrator" => "flow",
        "editor" => "code",
        "runtime" => "server",
        "shared" => "people",
        "chat" => "chat",
        _ => "wrench"
    };

    private static Dictionary<string, Shape[]> Load(string resource)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
        var raw = JsonSerializer.Deserialize<Dictionary<string, Part[]>>(s, _json)!;
        return raw.ToDictionary(kv => kv.Key, kv => kv.Value.Select(p => new Shape(Geometry.Parse(p.D), p.O ?? 1)).ToArray());
    }
}
