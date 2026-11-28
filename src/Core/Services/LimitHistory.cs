// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>One reading of an app's limit windows, at the time it was taken (local time).</summary>
public record LimitReading(DateTime At, List<LimitWindow> Windows);

/// <summary>
/// Every limit reading, appended to one file per month in <c>cache/limits/YYYY-MM.jsonl</c> (one <c>{at, app, windows}</c> per line, never rewritten):
/// providers only say how much is used now, so the pace chart is drawn from the readings taken while AI Stackprint runs (in the tray too) and, for Codex,
/// those in its own logs. The last <see cref="Keep"/> days are read; files older than <see cref="Retain"/> are deleted. Machine only, never exported.
/// The one file of earlier versions (<c>cache/limit-history.json</c>) is moved in once.
/// </summary>
public sealed class LimitHistory(string folder, Func<DateTime>? clock = null)
{
    /// <summary>A weekly window and one more day, so the chart starts at its reset.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(8);

    /// <summary>How long the monthly files are kept.</summary>
    public static readonly TimeSpan Retain = TimeSpan.FromDays(90);

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);
    private readonly Lock _lock = new();
    private Dictionary<string, List<LimitReading>>? _apps;

    private sealed record Line(DateTime At, string App, List<LimitWindow> Windows);

    public LimitHistory() : this(Path.Combine(DataFolder.Cache, "limits")) { }

    /// <summary>An app's readings, oldest first.</summary>
    public IReadOnlyList<LimitReading> Readings(string appId)
    {
        lock (_lock)
        {
            return [.. Apps().GetValueOrDefault(appId) ?? []];
        }
    }

    /// <summary>Adds readings (a repeat of one already kept is skipped), appends the new ones to their month's file and forgets those older than <see cref="Keep"/>.</summary>
    public void Add(string appId, IEnumerable<LimitReading> readings, DateTime now)
    {
        lock (_lock)
        {
            var apps = Apps();
            var list = apps.GetValueOrDefault(appId) ?? [];
            var added = new List<LimitReading>();
            foreach (var r in readings.Where(r => r.Windows.Count > 0 && now - r.At < Keep))
            {
                if (!list.Any(o => Math.Abs((o.At - r.At).TotalSeconds) < 1))
                {
                    list.Add(r);
                    added.Add(r);
                }
            }

            apps[appId] = [.. list.Where(r => now - r.At < Keep).OrderBy(r => r.At)];
            Append(added.Select(r => new Line(r.At, appId, r.Windows)));
        }
    }

    public void Add(string appId, LimitReading reading, DateTime now) => Add(appId, [reading], now);

    private string File(DateTime at) => Path.Combine(folder, at.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl");

    private void Append(IEnumerable<Line> lines)
    {
        try
        {
            foreach (var month in lines.GroupBy(l => File(l.At)))
            {
                Directory.CreateDirectory(folder);
                var text = new StringBuilder();
                foreach (var l in month.OrderBy(l => l.At))
                {
                    text.Append(JsonSerializer.Serialize(l, _json)).Append('\n');
                }

                System.IO.File.AppendAllText(month.Key, text.ToString());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // kept for this run only
    }

    private Dictionary<string, List<LimitReading>> Apps()
    {
        if (_apps != null)
        {
            return _apps;
        }

        var now = _clock();
        MoveOld();
        _apps = [];
        foreach (var (file, month) in Months())
        {
            if (month.AddMonths(1) < now - Retain)
            {
                Delete(file);
                continue;
            }

            if (month.AddMonths(1) < now - Keep)
            {
                continue;
            }

            foreach (var l in Read(file).Where(l => l.Windows.Count > 0 && now - l.At < Keep))
            {
                if (!_apps.TryGetValue(l.App, out var list))
                {
                    _apps[l.App] = list = [];
                }

                if (!list.Any(o => Math.Abs((o.At - l.At).TotalSeconds) < 1))
                {
                    list.Add(new LimitReading(l.At, l.Windows));
                }
            }
        }

        foreach (var k in _apps.Keys.ToList())
        {
            _apps[k] = [.. _apps[k].OrderBy(r => r.At)];
        }

        return _apps;
    }

    private IEnumerable<(string File, DateTime Month)> Months()
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.GetFiles(folder, "*.jsonl")
            .Select(f => (f, ok: DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var m), m))
            .Where(t => t.ok).Select(t => (t.f, t.m));
    }

    private static List<Line> Read(string file)
    {
        var lines = new List<Line>();
        try
        {
            foreach (var text in System.IO.File.ReadLines(file))
            {
                try
                {
                    if (text.Length > 0 && JsonSerializer.Deserialize<Line>(text, _json) is { App: not null, Windows: not null } l)
                    {
                        lines.Add(l);
                    }
                }
                catch (JsonException) { }   // a line cut short (the app stopped while writing): the others are kept
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return lines;
    }

    /// <summary>The single file of versions before the monthly files: its readings are appended, then it is deleted.</summary>
    private void MoveOld()
    {
        var old = Path.Combine(Path.GetDirectoryName(folder.TrimEnd('/', '\\')) ?? folder, "limit-history.json");
        if (!System.IO.File.Exists(old))
        {
            return;
        }

        try
        {
            using (var s = System.IO.File.OpenRead(old))
            {
                var apps = JsonSerializer.Deserialize<Dictionary<string, List<LimitReading>>>(s, _json) ?? [];
                Append(apps.SelectMany(a => a.Value.Where(r => r.Windows is { Count: > 0 }).Select(r => new Line(r.At, a.Key, r.Windows))));
            }

            Delete(old);
        }
        catch (JsonException) { Delete(old); }   // unreadable: nothing to keep
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // try again next run
    }

    private static void Delete(string file)
    {
        try
        {
            System.IO.File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
