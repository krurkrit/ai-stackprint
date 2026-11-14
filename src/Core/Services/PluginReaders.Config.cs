// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>OpenCode plugin files: each .js / .ts file, or package folder, in a plugins folder (~/.config/opencode/plugins, .opencode/plugins).</summary>
internal sealed class OpenCodeFileReader : IPluginReader
{
    private static readonly string[] _code = [".js", ".mjs", ".cjs", ".ts", ".mts"];
    private static readonly string[] _manifest = ["package.json"];

    public string Format => "opencode-files";

    public void Read(string path, PluginContext ctx, List<Item> rows)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var f in Directory.EnumerateFiles(path).Where(f => _code.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
            {
                rows.Add(new Item("Plugin", Path.GetFileNameWithoutExtension(f), "", ctx.App.Name, ctx.Level, f) { App = ctx.App.Id, Project = ctx.Project, LevelLabel = ctx.Source?.Label });
            }

            foreach (var d in Directory.EnumerateDirectories(path).Where(d => !Path.GetFileName(d).StartsWith('.')))
            {
                PluginManifests.Add(rows, ctx, d, manifests: _manifest);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>OpenCode npm plugins named in opencode.json(c): "plugin" or "plugins", each a package name ("name@1.2.0") or { "package": … }.</summary>
internal sealed class OpenCodeConfigReader : IPluginReader
{
    public string Format => "opencode-config";

    public void Read(string path, PluginContext ctx, List<Item> rows)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var key in new[] { "plugin", "plugins" })
            {
                if (!doc.RootElement.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var e in list.EnumerateArray())
                {
                    var spec = e.ValueKind == JsonValueKind.String ? e.GetString()
                        : e.ValueKind == JsonValueKind.Object && e.TryGetProperty("package", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                    if (string.IsNullOrWhiteSpace(spec))
                    {
                        continue;
                    }

                    var at = spec.LastIndexOf('@');   // "@scope/name@1.2.0": the version follows the last @, never the first
                    var (name, version) = at > 0 ? (spec[..at], spec[(at + 1)..]) : (spec, null);
                    rows.Add(new Item("Plugin", name, "npm package named in " + Path.GetFileName(path), ctx.App.Name, ctx.Level, path)
                    { App = ctx.App.Id, Project = ctx.Project, Version = version, LevelLabel = ctx.Source?.Label });
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Goose extensions in config.yaml: an "extensions:" map of name → enabled, type, name, display_name, description, bundled.
/// Only that block's simple key: value lines are read. Extensions that start a program or reach a server are MCP servers.
/// </summary>
internal sealed class GooseExtensionReader : IPluginReader
{
    private static readonly string[] _mcp = ["stdio", "sse", "streamable_http", "streamablehttp"];

    public string Format => "goose";

    public void Read(string path, PluginContext ctx, List<Item> rows)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var inside = false;
            Dictionary<string, string>? ext = null;
            string? key = null;
            void Flush()
            {
                if (key == null || ext == null)
                {
                    return;
                }

                rows.Add(new Item("Plugin", ext.GetValueOrDefault("display_name") ?? ext.GetValueOrDefault("name") ?? key, MarkdownLite.Plain(ext.GetValueOrDefault("description") ?? ""), ctx.App.Name, ctx.Level, path)
                {
                    App = ctx.App.Id,
                    LevelLabel = ctx.Source?.Label,
                    Scope = ext.GetValueOrDefault("enabled") == "false" ? "disabled" : null,
                    Bundled = ext.GetValueOrDefault("bundled") == "true",
                    Provides = _mcp.Contains(ext.GetValueOrDefault("type")) ? ["mcp"] : []
                });
            }

            foreach (var line in File.ReadLines(path))
            {
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                {
                    Flush(); key = null;
                    inside = line.TrimEnd() == "extensions:";
                    continue;
                }

                var indent = line.Length - line.TrimStart().Length;
                var t = line.Trim();
                if (!inside || t.Length == 0 || t.StartsWith('#'))
                {
                    continue;
                }

                var colon = t.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                if (indent == 2)
                {
                    Flush();
                    key = t[..colon]; ext = new Dictionary<string, string>(StringComparer.Ordinal);
                }
                else if (indent == 4 && ext != null)
                {
                    ext.TryAdd(t[..colon], t[(colon + 1)..].Trim().Trim('\'', '"'));
                }
            }

            Flush();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Plugins an app lists as enabled in a settings file: "enabledPlugins": { "name@marketplace": true } (Factory Droid).</summary>
internal sealed class EnabledListReader : IPluginReader
{
    public string Format => "enabled-list";

    public void Read(string path, PluginContext ctx, List<Item> rows)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("enabledPlugins", out var list) || list.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var p in list.EnumerateObject())
            {
                rows.Add(new Item("Plugin", p.Name, "", ctx.App.Name, ctx.Level, path)
                { App = ctx.App.Id, Project = ctx.Project, LevelLabel = ctx.Source?.Label, Scope = p.Value.ValueKind == JsonValueKind.False ? "disabled" : null });
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
    }
}
