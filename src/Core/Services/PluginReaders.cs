// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Reads an app's plugin manifest. To support a new format: add a class and list it in PluginReaders.All.</summary>
public interface IPluginReader
{
    string Format { get; }
    void Read(string manifestPath, AppDefinition app, List<Item> rows);
}

public static class PluginReaders
{
    private static readonly IPluginReader[] _all = [new ClaudePluginReader()];
    public static IPluginReader? Find(string? format) => format == null ? null : _all.FirstOrDefault(r => r.Format == format);
}

/// <summary>~/.claude/plugins/installed_plugins.json: { "plugins": { "name@market": [ { scope, version, installPath } ] } }</summary>
internal class ClaudePluginReader : IPluginReader
{
    public string Format => "claude";

    public void Read(string file, AppDefinition app, List<Item> rows)
    {
        if (!File.Exists(file))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("plugins", out var ps))
            {
                return;
            }

            foreach (var p in ps.EnumerateObject())
            {
                var entries = p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().ToList() : [p.Value];
                foreach (var e in entries)
                {
                    string Str(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                    var path = Str("installPath");
                    var scope = Str("scope") is "" ? "user" : Str("scope");
                    var level = scope switch { "project" => "Project", "local" => "Local", "managed" => "System", _ => "User" };
                    var project = Str("projectPath") is "" ? null : Str("projectPath");
                    var (desc, author) = Manifest(path);
                    rows.Add(new Item("Plugin", p.Name, desc, app.Name, level, path)
                    { App = app.Id, Scope = scope, Project = project, Version = Str("version") is "" ? null : Str("version"), Author = author ?? Market(p.Name) });
                    if (path != "")
                    {
                        Scanner.Tagged(rows, () => Scanner.ReadRoot(path, new ScanRoot { Level = level, Depth = 6 }, $"{app.Name} · {p.Name.Split('@')[0]}", rows),
                            i => i with { App = app.Id, Plugin = p.Name, Project = project });
                    }
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Description and author from installPath/.claude-plugin/plugin.json; author is a string or { "name": … }.</summary>
    public static (string Description, string? Author) Manifest(string installPath)
    {
        var f = Path.Combine(installPath, ".claude-plugin", "plugin.json");
        if (installPath == "" || !File.Exists(f))
        {
            return ("", null);
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var r = doc.RootElement;
            var desc = r.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
            var author = r.TryGetProperty("author", out var a) ? a.ValueKind switch
            {
                JsonValueKind.String => a.GetString(),
                JsonValueKind.Object when a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String => n.GetString(),
                _ => null
            } : null;
            return (desc, string.IsNullOrWhiteSpace(author) ? null : author.Trim());
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return ("", null); }
    }

    /// <summary>The marketplace part of name@marketplace: the owner when the manifest names no author.</summary>
    private static string? Market(string id) => id.Contains('@') ? id[(id.IndexOf('@') + 1)..] : null;
}
