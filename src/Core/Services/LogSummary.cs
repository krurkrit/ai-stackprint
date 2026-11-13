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
    public const long TextBudget = 50L << 20;

    [GeneratedRegex(@"\b(ERROR|ERR|FATAL|CRITICAL|Exception)\b|level=error|""level"":\s*""error""", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLine();
    [GeneratedRegex(@"\b(WARN|WARNING)\b|level=warn|""level"":\s*""warn", RegexOptions.IgnoreCase)]
    private static partial Regex WarnLine();

    /// <summary>Only the newest part of a session file is parsed.</summary>
    public const int SessionBytes = 8 << 20;

    record SessionTotals(int Prompts, int ToolCalls, int SubAgentSteps, long InputTokens, long OutputTokens, Dictionary<string, int> Tools, List<string> Models);

    static readonly ConcurrentDictionary<(string, long, long), SessionTotals> _cache = new();

    static SessionTotals Totals(Session s) => new(
        s.Entries.Count(e => e.Role == "user"), s.Entries.Count(e => e.Role == "tool"), s.Entries.Count(e => e.SubAgent), s.InputTokens, s.OutputTokens,
        s.Entries.Where(e => e.Role == "tool" && e.Tool != null).GroupBy(e => e.Tool!).ToDictionary(g => g.Key, g => g.Count()), s.Models.ToList());

    /// <param name="since">Only files modified after this time; null = all.</param>
    public static LogStats Compute(IReadOnlyList<LogFile> all, DateTime? since, CancellationToken ct = default)
    {
        var files = all.Where(f => since == null || f.Modified >= since).ToList();
        int errors = 0, warnings = 0; long budget = TextBudget; bool capped = false;
        var sessions = new List<SessionTotals>();
        foreach (var f in files.OrderByDescending(f => f.Modified))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (LogParsers.Find(f.Format) is { } parser)
                {
                    if (_cache.Count > 2000)
                    {
                        _cache.Clear();
                    }

                    sessions.Add(_cache.GetOrAdd((f.Path, f.Size, f.Modified.Ticks), _ => Totals(parser.Parse(LogReader.Lines(f.Path, SessionBytes)))));
                }
                else if (budget - f.Size < 0)
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
            files.OrderByDescending(f => f.Size).Take(3).ToList(), errors, warnings, capped,
            sessions.Count, sessions.Sum(t => t.Prompts), sessions.Sum(t => t.ToolCalls), sessions.Sum(t => t.SubAgentSteps),
            sessions.Sum(t => t.InputTokens), sessions.Sum(t => t.OutputTokens),
            sessions.SelectMany(t => t.Tools).GroupBy(t => t.Key).Select(g => (g.Key, g.Sum(t => t.Value))).OrderByDescending(t => t.Item2).Take(5).ToList(),
            sessions.SelectMany(t => t.Models).Distinct().Order().ToList());
    }
}
