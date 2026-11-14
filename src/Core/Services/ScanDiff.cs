// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <param name="What">Added, Removed or Edited (config files whose time changed).</param>
public record Change(string What, string Kind, string Name, string App, string Path);

/// <param name="Since">When the scan compared against was taken; null on the first run.</param>
public record ScanChanges(List<Change> Changes, DateTime? Since);

/// <summary>Skills, plugins and config files added, removed or edited since the scan before. The last scan is saved next to settings.json.</summary>
public static class ScanDiff
{
    public static string File => Path.Combine(AppCatalog.Folder, "last-scan.json");

    private sealed record Entry(string Kind, string Name, string App, string Path, [property: System.Text.Json.Serialization.JsonPropertyName("Stamp")] long Ticks);   // "Stamp" in last-scan.json
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

    /// <summary>Compares <paramref name="items"/> with the saved scan, then saves them as the new one.</summary>
    public static ScanChanges Update(IEnumerable<Item> items, string file)
    {
        var now = items.Where(i => i.Kind is "Skill" or "Plugin" or "Config" or "Command" or "Agent").Select(i => new Entry(i.Kind, i.Name, i.App, i.Path, Stamp(i))).DistinctBy(e => (e.Kind, e.Path, e.Name)).ToList();
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
                else if (b.Ticks != e.Ticks)
                {
                    changes.Add(new("Edited", e.Kind, e.Name, e.App, e.Path));
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
}
