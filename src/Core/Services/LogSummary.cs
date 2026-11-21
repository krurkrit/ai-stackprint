// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Totals for one app's logs over a period. Session fields stay 0 for apps without a session parser.</summary>
public record LogStats(int Files, long Bytes, DateTime? Newest, DateTime? Oldest, List<LogFile> Largest,
    int Errors, int Warnings, bool TextCapped,
    int Sessions, int Prompts, int ToolCalls, int SubAgentSteps, long InputTokens, long OutputTokens,
    List<(string Tool, int Count)> TopTools, List<string> Models);

/// <summary>Builds <see cref="LogStats"/>. Per-file session totals (not the parsed text) are cached by path + size + time, so re-opening is instant and the cache stays tiny.</summary>
public static partial class LogSummary
{
    /// <summary>Plain-text logs are scanned for errors/warnings up to this many bytes in total.</summary>
    internal const long TextBudget = 50L << 20;

    [GeneratedRegex(@"\b(ERROR|ERR|FATAL|CRITICAL|Exception)\b|level=error|""level"":\s*""error""", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLine();
    [GeneratedRegex(@"\b(WARN|WARNING)\b|level=warn|""level"":\s*""warn", RegexOptions.IgnoreCase)]
    private static partial Regex WarnLine();

    /// <summary>Only the newest part of a session file is parsed.</summary>
    internal const int SessionBytes = 8 << 20;

    private static readonly ConcurrentDictionary<(string, long, long), SessionCounts> _cache = new();

    /// <param name="since">Only files modified after this time; null = all.</param>
    public static LogStats Compute(IReadOnlyList<LogFile> all, DateTime? since, CancellationToken ct)
    {
        var files = all.Where(f => since == null || f.Modified >= since).ToList();
        int errors = 0, warnings = 0;
        var budget = TextBudget;
        var capped = false;
        if (_cache.Count > 2000)
        {
            _cache.Clear();
        }

        var sessions = Sessions(files, ct);
        foreach (var f in files.Where(f => LogParsers.Find(f.Format) == null).OrderByDescending(f => f.Modified))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (budget - f.Size < 0)
                {
                    capped = true;
                }
                else
                {
                    budget -= f.Size;
                    foreach (var line in LogReader.Lines(f.Path, long.MaxValue))
                    {
                        if (ErrorLine().IsMatch(line))
                        {
                            errors++;
                        }
                        else if (WarnLine().IsMatch(line))
                        {
                            warnings++;
                        }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // skip files that vanished or are locked
        }
        return new LogStats(files.Count, files.Sum(f => f.Size), files.MaxBy(f => f.Modified)?.Modified, files.MinBy(f => f.Modified)?.Modified,
            [.. files.OrderByDescending(f => f.Size).Take(3)], errors, warnings, capped,
            sessions.Count, sessions.Sum(t => t.Prompts), sessions.Sum(t => t.ToolCalls), sessions.Sum(t => t.SubAgentSteps),
            sessions.Sum(t => t.InputTokens), sessions.Sum(t => t.OutputTokens),
            [.. sessions.SelectMany(t => t.Tools).GroupBy(t => t.Key).Select(g => (g.Key, g.Sum(t => t.Value))).OrderByDescending(t => t.Item2).Take(5)],
            [.. sessions.SelectMany(t => t.Models).Distinct().Order()]);
    }

    public static LogStats Compute(IReadOnlyList<LogFile> all, DateTime? since) => Compute(all, since, default);

    /// <summary>The totals of every session file, several files at once: counting is CPU-bound and the files are independent.</summary>
    private static List<SessionCounts> Sessions(List<LogFile> files, CancellationToken ct)
    {
        var parsed = files.Select(f => (File: f, Parser: LogParsers.Find(f.Format))).Where(p => p.Parser != null).ToList();
        var totals = new SessionCounts?[parsed.Count];
        Parallel.For(0, parsed.Count, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4)
        }, i =>
        {
            var (f, parser) = parsed[i];
            try
            {
                totals[i] = _cache.GetOrAdd((f.Path, f.Size, f.Modified.Ticks), _ => parser!.Count(LogReader.Utf8Lines(f.Path, SessionBytes)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // skip files that vanished or are locked
        });
        return [.. totals.OfType<SessionCounts>()];
    }
}
