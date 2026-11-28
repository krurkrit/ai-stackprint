// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>One reading of an app's limit windows, at the time it was taken (local time).</summary>
public record LimitReading(DateTime At, List<LimitWindow> Windows);

/// <summary>
/// The limit readings of the last <see cref="Keep"/> days, per app, in <c>cache/limit-history.json</c>: providers only say how much is used now,
/// so the pace chart is drawn from the readings taken while AI Stackprint runs (and, for Codex, those in its own logs). Machine only, never exported.
/// </summary>
public sealed class LimitHistory(string file)
{
    /// <summary>A weekly window and one more day, so the chart starts at its reset.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(8);
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly Lock _lock = new();
    private Dictionary<string, List<LimitReading>>? _apps;

    public LimitHistory() : this(Path.Combine(DataFolder.Cache, "limit-history.json")) { }

    /// <summary>An app's readings, oldest first.</summary>
    public IReadOnlyList<LimitReading> Readings(string appId)
    {
        lock (_lock)
        {
            return [.. Apps().GetValueOrDefault(appId) ?? []];
        }
    }

    /// <summary>Adds readings (a repeat of one already kept is skipped), drops those older than <see cref="Keep"/> and saves.</summary>
    public void Add(string appId, IEnumerable<LimitReading> readings, DateTime now)
    {
        lock (_lock)
        {
            var apps = Apps();
            var list = apps.GetValueOrDefault(appId) ?? [];
            var added = false;
            foreach (var r in readings.Where(r => r.Windows.Count > 0 && now - r.At < Keep))
            {
                if (!list.Any(o => Math.Abs((o.At - r.At).TotalSeconds) < 1))
                {
                    list.Add(r);
                    added = true;
                }
            }

            var kept = list.Where(r => now - r.At < Keep).OrderBy(r => r.At).ToList();
            if (!added && kept.Count == list.Count)
            {
                return;
            }

            apps[appId] = kept;
            Save(apps);
        }
    }

    public void Add(string appId, LimitReading reading, DateTime now) => Add(appId, [reading], now);

    private Dictionary<string, List<LimitReading>> Apps()
    {
        if (_apps != null)
        {
            return _apps;
        }

        try
        {
            using var s = File.OpenRead(file);
            _apps = JsonSerializer.Deserialize<Dictionary<string, List<LimitReading>>>(s, _json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }   // none yet, or unreadable: start again

        return _apps ??= [];
    }

    private void Save(Dictionary<string, List<LimitReading>> apps)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(apps, _json));
            File.Move(tmp, file, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // kept for this run only
    }
}
