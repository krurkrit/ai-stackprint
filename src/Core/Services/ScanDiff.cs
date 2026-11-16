// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <param name="What">Added, Removed or Edited (config files whose time changed; skills and plugins whose files or version changed; servers and hooks whose definition changed).</param>
public record Change(string What, string Kind, string Name, string App, string Path)
{
    /// <summary>What changed in an edit: "version 1.2 → 1.3", "files changed", "command or address changed", "command changed"; null when only the time changed.</summary>
    public string? Detail { get; init; }
}

/// <param name="Since">When the scan compared against was taken; null on the first run.</param>
public record ScanChanges(List<Change> Changes, DateTime? Since);

/// <summary>Skills, plugins, commands, agents, config files, servers and hooks added, removed or edited since the scan before. The last scan is saved in the data folder's cache.</summary>
public static class ScanDiff
{
    public static string File => Path.Combine(DataFolder.Cache, "last-scan.json");

    // "Stamp" in last-scan.json. Hash: fingerprint of a skill, plugin or MCP server definition (never its content); null in older files = unknown
    private sealed record Entry(string Kind, string Name, string App, string Path, [property: System.Text.Json.Serialization.JsonPropertyName("Stamp")] long Ticks, string? Hash = null, string? Version = null);
    private sealed record Snapshot(DateTime Time, List<Entry> Items);

    private static long Stamp(Item i)
    {
        if (i.Kind != "Config" || i.AppWritten)
        {
            return 0;   // AppWritten: the app saves it on its own, so only added or removed counts
        }

        try { return System.IO.File.GetLastWriteTimeUtc(i.Path).Ticks; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Compares <paramref name="items"/>, <paramref name="servers"/> and <paramref name="hooks"/> with the saved scan, then saves them as the new one.</summary>
    public static ScanChanges Update(IEnumerable<Item> items, string file, IEnumerable<McpServer>? servers = null, IEnumerable<Hook>? hooks = null)
    {
        var now = items.Where(i => i.Kind is "Skill" or "Plugin" or "Config" or "Command" or "Agent").Select(i => new Entry(i.Kind, i.Name, i.App, i.Path, Stamp(i), i.Kind is "Skill" or "Plugin" ? Fingerprint.Of(i) : null, i.Version))
            .Concat((servers ?? []).Select(s => new Entry(s.Kind + " server", s.Project == null ? s.Name : $"{s.Name} · {System.IO.Path.GetFileName(s.Project.TrimEnd(System.IO.Path.DirectorySeparatorChar, '/'))}", s.Apps.FirstOrDefault() ?? "", s.File, 0, s.Fingerprint)))
            .Concat((hooks ?? []).GroupBy(h => (h.File, h.Name)).SelectMany(g => g.Select((h, n) => new Entry("Hook", n == 0 ? h.Name : $"{h.Name} ({n + 1})", h.Apps.FirstOrDefault() ?? "", h.File, 0, h.Fingerprint))))
            .DistinctBy(e => (e.Kind, e.Path, e.Name)).ToList();
        Snapshot? old = null;
        try
        {
            if (System.IO.File.Exists(file))
            {
                old = JsonSerializer.Deserialize<Snapshot>(System.IO.File.ReadAllText(file));
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        var changes = new List<Change>();
        if (old != null)
        {
            var before = old.Items.ToDictionary(e => (e.Kind, e.Path, e.Name));
            var after = now.ToDictionary(e => (e.Kind, e.Path, e.Name));
            foreach (var e in now)
            {
                if (!before.TryGetValue((e.Kind, e.Path, e.Name), out var b))
                {
                    changes.Add(new("Added", e.Kind, e.Name, e.App, e.Path));
                }
                else if (Edit(b, e) is { } detail)
                {
                    changes.Add(new Change("Edited", e.Kind, e.Name, e.App, e.Path) { Detail = detail == "" ? null : detail });
                }
            }

            foreach (var e in old.Items.Where(e => !after.ContainsKey((e.Kind, e.Path, e.Name))))
            {
                changes.Add(new("Removed", e.Kind, e.Name, e.App, e.Path));
            }
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            System.IO.File.WriteAllText(file, JsonSerializer.Serialize(new Snapshot(DateTime.Now, now)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new(changes, old?.Time);
    }

    /// <summary>Null when nothing changed; otherwise what changed ("" when only the time did).</summary>
    private static string? Edit(Entry b, Entry e)
    {
        if (e.Kind is "MCP server" or "LSP server" or "Hook")
        {
            return b.Hash != null && e.Hash != null && b.Hash != e.Hash ? (e.Kind == "Hook" ? "command changed" : "command or address changed") : null;
        }

        if (b.Version != null && e.Version != null && b.Version != e.Version)
        {
            return $"version {b.Version} → {e.Version}";
        }

        if (b.Hash != null && e.Hash != null && b.Hash != e.Hash)
        {
            return "files changed";
        }

        return b.Ticks != e.Ticks ? "" : null;
    }
}
