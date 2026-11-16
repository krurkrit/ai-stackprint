// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

using static AiStackprint.Services.ConfigJson;

namespace AiStackprint.Services;

public static partial class McpServers
{
    /// <summary>
    /// The MCP and LSP servers plugins add, as rows of their plugin: .mcp.json and .lsp.json in the plugin folder, the manifest's "mcpServers" and
    /// "lspServers" (inline, or paths to such files) and an MCP bundle's server.mcp_config. Placeholders such as ${CLAUDE_PLUGIN_ROOT} are shown as written.
    /// </summary>
    public static List<McpServer> FromPlugins(IEnumerable<Item> items)
    {
        var res = new List<McpServer>();
        foreach (var p in items.Where(i => i.Kind == "Plugin" && i.Path != "" && Directory.Exists(i.Path)))
        {
            var note = p.Scope == "disabled" ? "plugin disabled" : null;
            res.AddRange(InPlugin(p.Path).Select(s => s with
            {
                Apps = [p.App],
                Level = p.Level,
                LevelLabel = p.LevelLabel,
                Project = p.Project,
                Plugin = p.Name,
                Note = note == null ? s.Note : s.Note == null ? note : $"{s.Note} · {note}",
            }));
        }

        return res;
    }

    /// <summary>The servers one plugin folder adds, each file once.</summary>
    internal static List<McpServer> InPlugin(string folder)
    {
        var res = new List<McpServer>();
        var read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void FromFile(string path, string kind)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full) && ContextBudget.Inside(full, folder) && read.Add(full) && Root(full) is { } root)
            {
                res.AddRange(Servers(root.RootElement, kind, full));
                root.Dispose();
            }
        }

        FromFile(Path.Combine(folder, ".mcp.json"), "MCP");
        FromFile(Path.Combine(folder, ".lsp.json"), "LSP");
        if (PluginManifests.Find(folder) is { } manifest && manifest.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Root(manifest) is { } doc)
        {
            using (doc)
            {
                var r = doc.RootElement;
                foreach (var (key, kind) in new[] { ("mcpServers", "MCP"), ("lspServers", "LSP") })
                {
                    if (r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object)
                    {
                        res.AddRange(Map(v, kind, manifest));
                    }
                    else if (r.TryGetProperty(key, out v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Array)
                    {
                        foreach (var f in v.ValueKind == JsonValueKind.String ? [v] : v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String))
                        {
                            FromFile(Path.Combine(folder, f.GetString()!.Replace('/', Path.DirectorySeparatorChar)), kind);
                        }
                    }
                }

                // An MCP bundle (Claude Desktop extension): one server, named after the bundle
                if (r.TryGetProperty("server", out var server) && server.ValueKind == JsonValueKind.Object
                    && server.TryGetProperty("mcp_config", out var config) && config.ValueKind == JsonValueKind.Object
                    && One(Str(r, "name") ?? Path.GetFileName(folder), config, manifest) is { } one)
                {
                    res.Add(one);
                }
            }
        }

        return res;
    }

    /// <summary>A server file: { "mcpServers": { … } } (or "lspServers"), or the map itself.</summary>
    private static IEnumerable<McpServer> Servers(JsonElement root, string kind, string path)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var key = kind == "LSP" ? "lspServers" : "mcpServers";
        return root.TryGetProperty(key, out var inner) && inner.ValueKind == JsonValueKind.Object ? Map(inner, kind, path) : Map(root, kind, path);
    }

    private static IEnumerable<McpServer> Map(JsonElement map, string kind, string path) =>
        map.EnumerateObject().Where(s => s.Value.ValueKind == JsonValueKind.Object).Select(s => One(s.Name, s.Value, path)).OfType<McpServer>().Select(s => s with { Kind = kind });

    private static JsonDocument? Root(string path)
    {
        try { return JsonDocument.Parse(ContextBudget.ReadText(path), Jsonc); }
        catch (JsonException) { return null; }
    }
}
