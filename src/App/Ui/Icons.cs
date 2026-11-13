using System.Reflection;
using System.Text.Json;
using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>
/// Vector icons on a 24x24 grid, loaded from embedded JSON.
/// icons.json: Fluent UI System Icons (MIT). logos.json: LobeHub Icons (MIT) and Simple Icons (CC0); brand logos are trademarks of their owners.
/// </summary>
static class Icons
{
    public record Part(string D, double? O);
    public record Shape(Geometry Data, double Opacity);

    static readonly Dictionary<string, Shape[]> Ui = Load("icons.json"), Logos = Load("logos.json");

    /// <summary>UI icon by name (file name without _24_regular). Throws on unknown names: a typo is a bug.</summary>
    public static Shape[] Get(string name) => Ui[name];

    public static Shape[]? Logo(string? key) => key != null && Logos.TryGetValue(key, out var s) ? s : null;

    public static string ForCategory(string category) => category switch
    {
        "agent" => "bot", "orchestrator" => "flow", "editor" => "code", "runtime" => "server",
        "shared" => "people", "chat" => "chat", _ => "wrench"
    };

    static Dictionary<string, Shape[]> Load(string resource)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
        var raw = JsonSerializer.Deserialize<Dictionary<string, Part[]>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return raw.ToDictionary(kv => kv.Key, kv => kv.Value.Select(p => new Shape(Geometry.Parse(p.D), p.O ?? 1)).ToArray());
    }
}
