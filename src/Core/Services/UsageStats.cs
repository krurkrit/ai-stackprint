// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>One billed model response. Input excludes cached tokens; cache read/write are separate. Reasoning is informational (already inside Output). Cost: USD the app itself recorded, null when it did not.</summary>
/// <param name="CacheWrite1h">The part of <paramref name="CacheWrite"/> written to the 1-hour cache (Anthropic); the rest is the short cache.</param>
/// <param name="Fast">Fast mode (Claude <c>speed: fast</c>); <paramref name="UsOnly"/>: US-only processing. Both multiply the price.</param>
/// <param name="WebSearches">Web searches the model ran for this reply (priced per search).</param>
public record UsageEvent(DateTime Time, string Model, string Project, long Input, long Output, long CacheRead, long CacheWrite, long Reasoning, bool SubAgent, decimal? Cost = null,
    long CacheWrite1h = 0, bool Fast = false, bool UsOnly = false, int WebSearches = 0)
{
    /// <summary>The request's input size, which picks a long-context price tier: input + cache read + cache write.</summary>
    public long RequestInput => Input + CacheRead + CacheWrite;
}

/// <summary>Usage of one day, model and project (what is kept per file instead of every event).</summary>
/// <param name="Tier">The long-context tier its replies were over (the tier's <c>tokens</c>; 0 = base price), from the model data when the log was read.</param>
/// <remarks>Split by everything that changes the price (tier, fast mode, US-only processing); prices are applied when shown, so a changed price needs no new read.</remarks>
public record UsageBucket(DateOnly Day, string Model, string Project, long Input, long Output, long CacheRead, long CacheWrite, long Reasoning, int Messages, int SubAgentMessages, decimal? Cost = null,
    long CacheWrite1h = 0, long Tier = 0, bool Fast = false, bool UsOnly = false, int WebSearches = 0);

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
public static partial class UsageStats
{
    /// <summary>One file read once: its buckets per day, and its events of the last 8 days (for <see cref="Live(IReadOnlyList{LogFile}, DateTime, CancellationToken)"/>).</summary>
    private sealed record Parsed(List<UsageBucket> Buckets, List<UsageEvent> Recent);

    private static readonly ConcurrentDictionary<(string, long, long, string), Parsed> _cache = new();

    /// <summary>The file's usage, read only when it is new or changed; <see cref="Compute(IReadOnlyList{LogFile}, DateTime, CancellationToken)"/> and <see cref="Live(IReadOnlyList{LogFile}, DateTime, CancellationToken)"/> share it, so a page reads each log once.</summary>
    private static Parsed Read(LogFile f, ILogParser parser)
    {
        if (_cache.Count > 2000)
        {
            _cache.Clear();
        }

        var table = ModelCatalog.Load();   // its tier thresholds split the buckets: changed thresholds read the logs again
        return _cache.GetOrAdd((f.Path, f.Size, f.Modified.Ticks, table.TierKey), _ =>
        {
            var recent = DateTime.Now.AddDays(-8);
            var events = parser.UsageFile(f.Path).ToList();
            return new(Bucket(events, table), [.. events.Where(e => e.Time >= recent)]);
        });
    }

    /// <summary>The app has a log or database whose usage can be read.</summary>
    public static bool Supports(AppDefinition d) => d.Logs.Concat(d.Usage).Any(l => LogParsers.Find(l.Format) != null);

    /// <param name="since">Only usage from this day on; DateTime.MinValue = all.</param>
    public static UsageResult Compute(IReadOnlyList<LogFile> files, DateTime since, CancellationToken ct)
    {
        var first = DateOnly.FromDateTime(since);
        var all = new List<UsageBucket>();
        int sessions = 0, withParser = 0;
        var capped = false;
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
                if (inRange.Count > 0)
                {
                    sessions++;
                    all.AddRange(inRange);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // vanished or locked
        }
        return new(all, sessions, withParser, capped);
    }

    public static UsageResult Compute(IReadOnlyList<LogFile> files, DateTime since) => Compute(files, since, default);

    /// <summary>Use in the last 5 hours and 7 days (exact, from event times), the current 5-hour block, and the latest limit readings.</summary>
    public static UsageLive Live(IReadOnlyList<LogFile> files, DateTime now, CancellationToken ct)
    {
        var withParser = files.Where(f => LogParsers.Find(f.Format) != null).ToList();
        var events = new List<UsageEvent>();
        var limits = new List<LimitSample>();
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
            try { limits.AddRange(LogParsers.Find(f.Format)!.Limits(LogReader.Utf8Lines(f.Path, 2 << 20))); }   // readings are near the end of a file
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        events.Sort((a, b) => a.Time.CompareTo(b.Time));

        DateTime? blockStart = null;
        var last = DateTime.MinValue;
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

    public static UsageLive Live(IReadOnlyList<LogFile> files, DateTime now) => Live(files, now, default);

    /// <summary>
    /// The model the app used last: the last <c>"model"</c> value in the end of its newest log, else (no such value there) the model with the most
    /// replies on the last day of that log. Null when no log names one. Reads only the end of a file first: the newest log is often large and in use.
    /// </summary>
    public static string? LastModel(IReadOnlyList<LogFile> files, CancellationToken ct)
    {
        foreach (var f in files.Where(f => LogParsers.Find(f.Format) != null).OrderByDescending(f => f.Modified).Take(5))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (TailModel(f.Path) is { } model)
                {
                    return model;
                }

                var buckets = Read(f, LogParsers.Find(f.Format)!).Buckets;
                if (buckets.Count > 0)
                {
                    var day = buckets.Max(b => b.Day);
                    return buckets.Where(b => b.Day == day).GroupBy(b => b.Model).MaxBy(g => g.Sum(b => b.Messages))!.Key;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    public static string? LastModel(IReadOnlyList<LogFile> files) => LastModel(files, default);

    private const int TailBytes = 256 * 1024;

    /// <summary>A JSON <c>"model": "…"</c> property (not one escaped inside a string; not a placeholder such as <c>&lt;synthetic&gt;</c>).</summary>
    [System.Text.RegularExpressions.GeneratedRegex("""(?<!\\)"model"\s*:\s*"([^"\\<\s][^"\\]*)"(?!\s*:)""", System.Text.RegularExpressions.RegexOptions.RightToLeft)]
    private static partial System.Text.RegularExpressions.Regex ModelProperty();

    private static string? TailModel(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = Math.Min(s.Length, TailBytes);
        s.Seek(-length, SeekOrigin.End);
        var buffer = new byte[length];
        s.ReadExactly(buffer);
        var m = ModelProperty().Match(System.Text.Encoding.UTF8.GetString(buffer));
        return m.Success ? m.Groups[1].Value : null;
    }

    public static WindowUse Sum(IEnumerable<UsageEvent> events)
    {
        long tokens = 0, reads = 0;
        var n = 0;
        decimal cost = 0;
        var priced = false;
        foreach (var e in events)
        {
            tokens += e.Input + e.Output + e.CacheWrite;
            reads += e.CacheRead;
            n++;
            if (ModelCatalog.Cost(e) is { } c)
            {
                cost += c;
                priced = true;
            }
        }
        return new(tokens, reads, n, priced ? cost : null);
    }

    /// <summary>Per day, model and project, split by price tier (from <paramref name="table"/>'s thresholds), fast mode and US-only processing.</summary>
    public static List<UsageBucket> Bucket(IEnumerable<UsageEvent> events, ModelTable? table) =>
        [.. events.GroupBy(e => (Day: DateOnly.FromDateTime(e.Time), e.Model, e.Project, Tier: ModelCatalog.TierOf(e.Model, e.RequestInput, table), e.Fast, e.UsOnly))
            .Select(g => new UsageBucket(g.Key.Day, g.Key.Model, g.Key.Project, g.Sum(e => e.Input), g.Sum(e => e.Output), g.Sum(e => e.CacheRead), g.Sum(e => e.CacheWrite),
                g.Sum(e => e.Reasoning), g.Count(), g.Count(e => e.SubAgent), g.All(e => e.Cost != null) ? g.Sum(e => e.Cost!.Value) : null,
                g.Sum(e => e.CacheWrite1h), g.Key.Tier, g.Key.Fast, g.Key.UsOnly, g.Sum(e => e.WebSearches)))];

    public static List<UsageBucket> Bucket(IEnumerable<UsageEvent> events) => Bucket(events, null);
}
