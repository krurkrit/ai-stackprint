// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>One billed model response. Input excludes cached tokens; cache read/write are separate. Reasoning is informational (already inside Output). Cost: USD the app itself recorded, null when it did not.</summary>
public record UsageEvent(DateTime Time, string Model, string Project, long Input, long Output, long CacheRead, long CacheWrite, long Reasoning, bool SubAgent, decimal? Cost = null);

/// <summary>Usage of one day, model and project (what is kept per file instead of every event).</summary>
public record UsageBucket(DateOnly Day, string Model, string Project, long Input, long Output, long CacheRead, long CacheWrite, long Reasoning, int Messages, int SubAgentMessages, decimal? Cost = null);

/// <summary>One reading of a usage limit written by the app: percent of the window used, and when the window resets.</summary>
public record LimitSample(DateTime Time, string Name, double UsedPercent, int WindowMinutes, DateTime ResetsAt);

/// <summary>Tokens (input + output + cache write; cache reads apart), messages and estimated cost for a time window.</summary>
public record WindowUse(long Tokens, long CacheRead, int Messages, decimal? Cost);

/// <param name="Limits">Latest reading per window, from the newest logs (empty for apps that do not log limits).</param>
/// <param name="Block">The current 5-hour block (starts at the first message after a 5-hour gap, on the hour); null when none is active.</param>
public record UsageLive(List<LimitSample> Limits, WindowUse Last5h, WindowUse Last7d, DateTime? BlockStart, WindowUse? Block);

/// <param name="Sessions">Files with usage in the period.</param>
/// <param name="Files">Log files that have a usage parser.</param>
/// <param name="Capped">Some file was longer than the part that is read.</param>
public record UsageResult(List<UsageBucket> Buckets, int Sessions, int Files, bool Capped, UsageLive? Live = null);

/// <summary>Usage per app from session logs. Buckets per file are cached by path + size + time, so re-opening is instant and the cache stays small.</summary>
public static class UsageStats
{
    /// <summary>One file read once: its buckets per day, and its events of the last 8 days (for <see cref="Live"/>).</summary>
    private sealed record Parsed(List<UsageBucket> Buckets, List<UsageEvent> Recent);

    private static readonly ConcurrentDictionary<(string, long, long), Parsed> _cache = new();

    /// <summary>The file's usage, read only when it is new or changed; <see cref="Compute"/> and <see cref="Live"/> share it, so a page reads each log once.</summary>
    private static Parsed Read(LogFile f, ILogParser parser)
    {
        if (_cache.Count > 2000)
        {
            _cache.Clear();
        }

        return _cache.GetOrAdd((f.Path, f.Size, f.Modified.Ticks), _ =>
        {
            var recent = DateTime.Now.AddDays(-8);
            var events = parser.UsageFile(f.Path).ToList();
            return new(Bucket(events), [.. events.Where(e => e.Time >= recent)]);
        });
    }

    /// <summary>The app has a log or database whose usage can be read.</summary>
    public static bool Supports(AppDefinition d) => d.Logs.Concat(d.Usage).Any(l => LogParsers.Find(l.Format) != null);

    /// <param name="since">Only usage from this day on; DateTime.MinValue = all.</param>
    public static UsageResult Compute(IReadOnlyList<LogFile> files, DateTime since, CancellationToken ct = default)
    {
        var first = DateOnly.FromDateTime(since);
        var all = new List<UsageBucket>(); int sessions = 0, withParser = 0; var capped = false;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            if (LogParsers.Find(f.Format) is not { } parser)
            {
                continue;
            }

            withParser++;
            if (f.Modified < since)
            {
                continue;   // nothing written since then
            }

            try
            {
                var buckets = Read(f, parser).Buckets;
                if (!parser.WholeFile && f.Size > LogSummary.SessionBytes)
                {
                    capped = true;
                }

                var inRange = buckets.Where(b => b.Day >= first).ToList();
                if (inRange.Count > 0) { sessions++; all.AddRange(inRange); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // vanished or locked
        }
        return new(all, sessions, withParser, capped);
    }

    /// <summary>Use in the last 5 hours and 7 days (exact, from event times), the current 5-hour block, and the latest limit readings.</summary>
    public static UsageLive Live(IReadOnlyList<LogFile> files, DateTime now, CancellationToken ct = default)
    {
        var withParser = files.Where(f => LogParsers.Find(f.Format) != null).ToList();
        var events = new List<UsageEvent>(); var limits = new List<LimitSample>();
        foreach (var f in withParser.Where(f => f.Modified >= now.AddDays(-8)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                events.AddRange(Read(f, LogParsers.Find(f.Format)!).Recent.Where(e => e.Time >= now.AddDays(-7)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        foreach (var f in withParser.Where(f => !LogParsers.Find(f.Format)!.WholeFile).OrderByDescending(f => f.Modified).Take(3))
        {
            ct.ThrowIfCancellationRequested();
            try { limits.AddRange(LogParsers.Find(f.Format)!.Limits(LogReader.Lines(f.Path, 2 << 20))); }   // readings are near the end of a file
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        events.Sort((a, b) => a.Time.CompareTo(b.Time));

        DateTime? blockStart = null; var last = DateTime.MinValue;
        foreach (var e in events)
        {
            if (blockStart == null || e.Time >= blockStart.Value.AddHours(5) || e.Time - last > TimeSpan.FromHours(5))
            {
                blockStart = new DateTime(e.Time.Year, e.Time.Month, e.Time.Day, e.Time.Hour, 0, 0, e.Time.Kind);
            }

            last = e.Time;
        }
        WindowUse? block = null;
        if (blockStart is { } bs && now < bs.AddHours(5))
        {
            block = Sum(events.Where(e => e.Time >= bs));
        }
        else
        {
            blockStart = null;
        }

        return new([.. limits.GroupBy(l => l.Name).Select(g => g.MaxBy(l => l.Time)!).OrderBy(l => l.WindowMinutes)],
            Sum(events.Where(e => e.Time >= now.AddHours(-5))), Sum(events), blockStart, block);
    }

    public static WindowUse Sum(IEnumerable<UsageEvent> events)
    {
        long tokens = 0, reads = 0; var n = 0; decimal cost = 0; var priced = false;
        foreach (var e in events)
        {
            tokens += e.Input + e.Output + e.CacheWrite; reads += e.CacheRead; n++;
            if ((e.Cost ?? Pricing.Cost(e.Model, e.Input, e.Output, e.CacheRead, e.CacheWrite)) is { } c) { cost += c; priced = true; }
        }
        return new(tokens, reads, n, priced ? cost : null);
    }

    public static List<UsageBucket> Bucket(IEnumerable<UsageEvent> events) =>
        [.. events.GroupBy(e => (Day: DateOnly.FromDateTime(e.Time), e.Model, e.Project))
            .Select(g => new UsageBucket(g.Key.Day, g.Key.Model, g.Key.Project, g.Sum(e => e.Input), g.Sum(e => e.Output), g.Sum(e => e.CacheRead), g.Sum(e => e.CacheWrite),
                g.Sum(e => e.Reasoning), g.Count(), g.Count(e => e.SubAgent), g.All(e => e.Cost != null) ? g.Sum(e => e.Cost!.Value) : null))];
}
