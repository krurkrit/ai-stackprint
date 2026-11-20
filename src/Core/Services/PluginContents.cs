// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>The plugin a row came from: its app, level, project and id (name@marketplace or the manifest's name).</summary>
public sealed record PluginOwner(AppDefinition App, string Level, string? Project, string Id)
{
    /// <summary>The Source shown for the plugin's rows: "Claude Code · name".</summary>
    public string Source => $"{App.Name} · {Id.Split('@')[0]}";
}

/// <summary>
/// What a plugin holds besides skills, as rows of that plugin (<see cref="Item.Plugin"/>): commands and agents from the default folders
/// (commands/, agents/) and from the folders or files the manifest names ("commands", "agents"), which add to the defaults.
/// </summary>
public static class PluginContents
{
    private static readonly JsonDocumentOptions _json = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static void Add(List<Item> rows, string folder, PluginManifest m, PluginOwner owner)
    {
        if (folder == "" || !Directory.Exists(folder))
        {
            return;
        }

        var named = Named(m.File);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, kind, pattern) in new[] { ("commands", "command", "*.md;*.toml"), ("agents", "agent", "*.md") })
        {
            foreach (var at in Places(folder, field, named))
            {
                var dir = Directory.Exists(at) ? at : Path.GetDirectoryName(at)!;
                var source = new PartSource
                {
                    Kind = kind,
                    Level = owner.Level,
                    Depth = 3,
                    Pattern = Directory.Exists(at) ? pattern : Path.GetFileName(at)
                };
                var start = rows.Count;
                Scanner.Tagged(rows, () => Scanner.ReadParts(dir, source, owner.Source, rows),
                    i => i with
                    {
                        App = owner.App.Id,
                        Plugin = owner.Id,
                        Project = owner.Project
                    });
                Dedupe(rows, start, seen);
            }
        }
    }

    /// <summary>The default folder, then each path the manifest names under <paramref name="field"/>, that exists inside the plugin.</summary>
    internal static IEnumerable<string> Places(string folder, string field, Dictionary<string, List<string>> named)
    {
        var all = new List<string> { Path.Combine(folder, field) };
        all.AddRange(named.GetValueOrDefault(field, []).Select(p => Path.GetFullPath(Path.Combine(folder, p.Replace('/', Path.DirectorySeparatorChar)))));
        return all.Where(p => (Directory.Exists(p) || File.Exists(p)) && ContextBudget.Inside(p, folder)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The manifest's string and string-list fields (paths), by name; empty for a manifest that is not JSON.</summary>
    internal static Dictionary<string, List<string>> Named(string? file)
    {
        var d = new Dictionary<string, List<string>>();
        if (file == null || !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return d;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), _json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return d;
            }

            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var list = p.Value.ValueKind switch
                {
                    JsonValueKind.String => [p.Value.GetString()!],
                    JsonValueKind.Array => p.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList(),
                    _ => []
                };
                if (list.Count > 0)
                {
                    d[p.Name] = list;
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return d;
    }

    /// <summary>A file named twice (a default folder and a manifest path) is one row.</summary>
    private static void Dedupe(List<Item> rows, int start, HashSet<string> seen)
    {
        for (var i = start; i < rows.Count;)
        {
            if (seen.Add(rows[i].Kind + "|" + rows[i].Path))
            {
                i++;
            }
            else
            {
                rows.RemoveAt(i);
            }
        }
    }
}
