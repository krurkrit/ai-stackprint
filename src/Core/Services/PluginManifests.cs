// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>What a plugin or extension says about itself, from whichever manifest it has.</summary>
public sealed record PluginManifest(string Description, string? Author, IReadOnlyList<string> Keywords, IReadOnlyList<string> Provides, string? Name = null, string? Version = null, string? File = null);

/// <summary>
/// Reads plugin and extension manifests of every app the same way: plugin.json (Claude, Codex, Cursor, Factory, Copilot),
/// gemini-extension.json, qwen-extension.json, openclaw.plugin.json, an MCP bundle's manifest.json, a VS Code package.json,
/// Hermes plugin.yaml and Zed extension.toml. Only top-level name, version, description, author and keywords are read.
/// </summary>
public static partial class PluginManifests
{
    /// <summary>Manifest files looked for in a plugin folder, first found wins.</summary>
    public static readonly IReadOnlyList<string> Known =
    [
        ".claude-plugin/plugin.json", ".codex-plugin/plugin.json", ".cursor-plugin/plugin.json", ".factory-plugin/plugin.json", ".plugin/plugin.json",
        ".github/plugin/plugin.json", "plugin.json", "gemini-extension.json", "qwen-extension.json", "openclaw.plugin.json", "plugin.yaml", "plugin.yml",
        "extension.toml", "extension.json", "manifest.json", "package.json",
    ];

    private static readonly JsonDocumentOptions _json = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>The first manifest of <paramref name="manifests"/> found in <paramref name="folder"/>, or null.</summary>
    public static string? Find(string folder, IReadOnlyList<string>? manifests) =>
        (manifests is { Count: > 0 } ? manifests : Known).Select(m => Path.Combine(folder, m.Replace('/', Path.DirectorySeparatorChar))).FirstOrDefault(File.Exists);

    public static string? Find(string folder) => Find(folder, null);

    /// <summary>The plugin in <paramref name="folder"/>: its manifest when it has one, and what it adds (<see cref="PluginParts"/>) either way.</summary>
    public static PluginManifest Read(string folder, IReadOnlyList<string>? manifests)
    {
        var file = folder == "" ? null : Find(folder, manifests);
        try
        {
            return Path.GetExtension(file)?.ToLowerInvariant() switch
            {
                ".json" => Json(folder, file!),
                ".yaml" or ".yml" => Flat(folder, file!, YamlLine()),
                ".toml" => Flat(folder, file!, TomlLine()),
                _ => new("", null, [], PluginParts.Find(folder, null))
            };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new("", null, [], PluginParts.Find(folder, null));
        }
    }

    public static PluginManifest Read(string folder) => Read(folder, null);

    private static PluginManifest Json(string folder, string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file), _json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object)
        {
            return new("", null, [], PluginParts.Find(folder, null));
        }

        var nls = Localized(folder);   // VS Code: "%displayName%" names a string in package.nls.json
        string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? (s.Length > 2 && s[0] == '%' && s[^1] == '%' ? nls.GetValueOrDefault(s[1..^1]) : s) : null;
        var ui = r.TryGetProperty("interface", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;   // Codex: interface.displayName
        var name = Str(r, "displayName") ?? Str(r, "display_name") ?? (ui.ValueKind == JsonValueKind.Object ? Str(ui, "displayName") : null) ?? Str(r, "name");
        var desc = Str(r, "description") ?? (ui.ValueKind == JsonValueKind.Object ? Str(ui, "shortDescription") : null) ?? "";
        var author = r.TryGetProperty("author", out var a) ? a.ValueKind switch
        {
            JsonValueKind.String => a.GetString(),
            JsonValueKind.Object => Str(a, "name"),
            _ => null
        } : null;
        author ??= Str(r, "publisher");
        var keywords = r.TryGetProperty("keywords", out var k) && k.ValueKind == JsonValueKind.Array
            ? k.EnumerateArray().Where(w => w.ValueKind == JsonValueKind.String).Select(w => w.GetString()!.Trim()).Where(w => w != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        return new(MarkdownLite.Plain(desc), string.IsNullOrWhiteSpace(author) ? null : author.Trim(), keywords, PluginParts.Find(folder, r), name, Str(r, "version"), file);
    }

    /// <summary>package.nls.json strings, for VS Code extensions whose package.json names them as %key%.</summary>
    private static Dictionary<string, string> Localized(string folder)
    {
        var f = Path.Combine(folder, "package.nls.json");
        if (!File.Exists(f))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(f), _json);
        return doc.RootElement.ValueKind != JsonValueKind.Object ? [] : doc.RootElement.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    /// <summary>Top-level key: value (YAML) or key = "value" (TOML) lines; a TOML file stops at its first [table].</summary>
    private static PluginManifest Flat(string folder, string file, Regex line)
    {
        var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in File.ReadLines(file))
        {
            if (l.StartsWith('['))
            {
                break;
            }

            if (line.Match(l) is { Success: true } m && m.Groups[2].Value.Trim() is not (">" or "|" or ">-" or "|-"))   // a YAML block value is on the next lines: not read
            {
                v.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Trim().Trim('"', '\''));
            }
        }

        // TOML authors = ["Jane <jane@x.org>"]: the first name, without the address
        var author = v.GetValueOrDefault("author") ?? (v.GetValueOrDefault("authors") is { } list ? list.Trim('[', ']').Split(',')[0].Trim().Trim('"').Split('<')[0].Trim() : null);
        return new(MarkdownLite.Plain(v.GetValueOrDefault("description") ?? ""), string.IsNullOrWhiteSpace(author) ? null : author, [], PluginParts.Find(folder, null),
            v.GetValueOrDefault("name") ?? v.GetValueOrDefault("id"), v.GetValueOrDefault("version"));
    }

    /// <summary>
    /// Adds the plugin in <paramref name="folder"/> as a row, and the skills in its skills folder as rows of that plugin.
    /// </summary>
    /// <param name="id">The app's own id for it (name@marketplace); otherwise the manifest's name, then the folder's.</param>
    /// <param name="read">Its manifest when the caller has read it already.</param>
    public static Item Add(List<Item> rows, PluginContext ctx, string folder, string? id, IReadOnlyList<string>? manifests,
        string? scope, bool bundled, string? version, PluginManifest? read)
    {
        var m = read ?? Read(folder, manifests ?? ctx.Source?.Manifests);
        var name = id ?? m.Name ?? Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        var item = new Item("Plugin", name, m.Description, ctx.App.Name, ctx.Level, folder)
        {
            App = ctx.App.Id,
            Project = ctx.Project,
            Scope = scope,
            Bundled = bundled,
            LevelLabel = ctx.Source?.Label,
            Version = version ?? m.Version,
            Author = m.Author,
            Keywords = m.Keywords,
            Provides = m.Provides
        };
        rows.Add(item);
        var skills = Path.Combine(folder, "skills");
        if (Directory.Exists(skills))
        {
            Scanner.Tagged(rows, () => Scanner.ReadRoot(skills, new SkillSource
            {
                Level = ctx.Level,
                Depth = 4
            }, $"{ctx.App.Name} · {name.Split('@')[0]}", rows),
                i => i with
                {
                    App = ctx.App.Id,
                    Plugin = name,
                    Project = ctx.Project
                });
        }

        PluginContents.Add(rows, folder, m, new(ctx.App, ctx.Level, ctx.Project, name));
        return item;
    }

    public static Item Add(List<Item> rows, PluginContext ctx, string folder, string? id, IReadOnlyList<string>? manifests, string? scope, bool bundled, string? version) => Add(rows, ctx, folder, id, manifests, scope, bundled, version, null);

    public static Item Add(List<Item> rows, PluginContext ctx, string folder, string? id, IReadOnlyList<string>? manifests, string? scope, bool bundled) => Add(rows, ctx, folder, id, manifests, scope, bundled, null, null);

    public static Item Add(List<Item> rows, PluginContext ctx, string folder, string? id, IReadOnlyList<string>? manifests, string? scope) => Add(rows, ctx, folder, id, manifests, scope, false, null, null);

    public static Item Add(List<Item> rows, PluginContext ctx, string folder, string? id, IReadOnlyList<string>? manifests) => Add(rows, ctx, folder, id, manifests, null, false, null, null);

    public static Item Add(List<Item> rows, PluginContext ctx, string folder, string? id) => Add(rows, ctx, folder, id, null, null, false, null, null);

    public static Item Add(List<Item> rows, PluginContext ctx, string folder) => Add(rows, ctx, folder, null, null, null, false, null, null);

    [GeneratedRegex(@"^([A-Za-z_][\w-]*):\s*(\S.*)$")]
    private static partial Regex YamlLine();

    [GeneratedRegex(@"^([A-Za-z_][\w-]*)\s*=\s*(\S.*)$")]
    private static partial Regex TomlLine();
}
