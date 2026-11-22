// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Claude Code keeps each sub-agent in its own file, <c>&lt;session&gt;/subagents/agent-&lt;id&gt;.jsonl</c>, next to the session's <c>&lt;session&gt;.jsonl</c>
/// (which has only the call that starts it and its result). This reads a session together with those files: their steps are put among the session's by time,
/// marked as sub-agent steps, so the graph shows the branches. Read-only; the steps of an agent have no line in the main file (Line 0), so they cannot be bookmarked.
/// </summary>
public static class SessionAgents
{
    /// <summary>At most this many agent files are read, each its newest <see cref="AgentBytes"/>.</summary>
    internal const int MaxAgents = 40;
    internal const long AgentBytes = 4L << 20;

    /// <summary>The session a log file holds, with its sub-agents for formats that keep them in files of their own.</summary>
    public static Session Read(LogFile f, long maxBytes)
    {
        var main = LogParsers.Find(f.Format)!.Parse(LogReader.Utf8Lines(f.Path, maxBytes));
        return f.Format == "claude-session" ? Merge(main, f.Path, new FileInfo(f.Path).Length > maxBytes) : main;
    }

    /// <summary>
    /// A long session is read cut to its end, so agents that ran earlier are not in it. This reads the whole file and returns the stretch around the newest agent
    /// that started before <paramref name="before"/> (and the agents running at the same time), a few main steps either side, with the same branches.
    /// Null when no agent started before that time. Its steps keep their real file lines; tokens are left out (they would be the whole session's).
    /// </summary>
    public static Session? ReadAround(LogFile f, DateTime before, int maxSteps)
    {
        var dir = Path.Combine(Path.GetDirectoryName(f.Path) ?? "", Path.GetFileNameWithoutExtension(f.Path), "subagents");
        if (f.Format != "claude-session" || !Directory.Exists(dir))
        {
            return null;
        }

        var parser = LogParsers.Find("claude-session")!;
        var agents = new List<(string File, DateTime First, DateTime Last)>();
        foreach (var file in Directory.EnumerateFiles(dir, "agent-*.jsonl").Order(StringComparer.Ordinal).Take(MaxAgents))
        {
            var times = parser.Parse(LogReader.Utf8Lines(file, AgentBytes)).Entries.Where(e => e.Time != null).Select(e => e.Time!.Value).ToList();
            if (times.Count > 0)
            {
                agents.Add((file, times.Min(), times.Max()));
            }
        }

        var target = agents.Where(a => a.First < before).OrderByDescending(a => a.First).FirstOrDefault();
        if (target.File == null)
        {
            return null;
        }

        var cluster = agents.Where(a => a.First <= target.Last && a.Last >= target.First).ToList();
        DateTime from = cluster.Min(a => a.First), to = cluster.Max(a => a.Last);
        var main = parser.Parse(LogReader.Utf8Lines(f.Path, new FileInfo(f.Path).Length));
        var first = main.Entries.FindIndex(e => e.Time >= from);
        var last = main.Entries.FindLastIndex(e => e.Time <= to);
        first = first < 0 ? main.Entries.Count - 1 : first;
        last = Math.Max(first, last);
        var lo = Math.Max(0, first - ContextSteps);
        var hi = Math.Min(main.Entries.Count - 1, last + ContextSteps);
        var part = main.Entries.Skip(lo).Take(hi - lo + 1).ToList();
        var shown = new Session(part, main.Models, 0, 0, part.FirstOrDefault(e => e.Time != null)?.Time, part.LastOrDefault(e => e.Time != null)?.Time);
        var merged = Merge(shown, f.Path, false, [.. cluster.Select(a => a.File)]);
        var entries = merged.Entries.Count > maxSteps ? merged.Entries.Take(maxSteps).ToList() : merged.Entries;   // the start stays: the fork is there
        return merged with { Entries = entries, InputTokens = 0, OutputTokens = 0, AgentsBefore = agents.Count(a => a.Last < from) };
    }

    /// <summary>
    /// The steps that start in the lines around <paramref name="line"/> of a session file (a search result in a long file, or one older than the steps shown), read from
    /// the file itself: their lines are the file's own. Null when the format has no session parser. Tokens are left out (they would be the whole session's).
    /// </summary>
    public static Session? ReadAroundLine(LogFile f, long line, int maxSteps)
    {
        if (LogParsers.Find(f.Format) is not { } parser)
        {
            return null;
        }

        var (text, first) = LogSearch.Window(f.Path, line, LineContext, LineContext);
        var lines = text.Split('\n');
        var parsed = parser.Parse(LogReader.Utf8(lines.Take(lines.Length - 1)));   // the text ends with a line break: no last empty line
        var entries = parsed.Entries.Select(e => e.Line is > 0 ? e with { Line = e.Line + first - 1 } : e).ToList();
        if (entries.Count > maxSteps)
        {
            var at = entries.FindLastIndex(e => e.Line is > 0 && e.Line <= line);
            var from = Math.Clamp(at - maxSteps / 2, 0, entries.Count - maxSteps);   // keep the step the result is in, near the middle
            entries = entries.Skip(from).Take(maxSteps).ToList();
        }

        return new Session(entries, parsed.Models, 0, 0, entries.FirstOrDefault(e => e.Time != null)?.Time, entries.LastOrDefault(e => e.Time != null)?.Time);
    }

    private const int LineContext = 150;   // lines read before and after the result
    private const int ContextSteps = 12;   // main steps shown before the first and after the last step of the agents

    /// <param name="cut">The session was read cut to its end: the agents' steps from before its first step are left out.</param>
    /// <param name="only">Read only these agent files; null: all.</param>
    internal static Session Merge(Session main, string path, bool cut, HashSet<string>? only = null)
    {
        var dir = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path), "subagents");
        if (!Directory.Exists(dir))
        {
            return main;
        }

        var parser = LogParsers.Find("claude-session")!;
        var from = cut ? main.Entries.FirstOrDefault(e => e.Time != null)?.Time : null;
        var sub = new List<LogEntry>();
        var models = new HashSet<string>(main.Models);
        var before = 0;
        long input = main.InputTokens, output = main.OutputTokens;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "agent-*.jsonl").Order(StringComparer.Ordinal).Take(MaxAgents).Where(p => only == null || only.Contains(p)))
            {
                var id = Path.GetFileNameWithoutExtension(file)["agent-".Length..];
                var s = parser.Parse(LogReader.Utf8Lines(file, AgentBytes));
                var inside = s.Entries.Where(e => from == null || e.Time == null || e.Time >= from).ToList();
                if (inside.Count == 0)
                {
                    before += s.Entries.Count > 0 ? 1 : 0;   // the agent ran before the part of the session that was read
                    continue;
                }

                sub.AddRange(inside.Select(e => e with { SubAgent = true, Agent = string.IsNullOrEmpty(e.Agent) ? id : e.Agent, Line = 0 }));
                models.UnionWith(s.Models);
                input += s.InputTokens;
                output += s.OutputTokens;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // an agent file that vanished or is locked is left out

        if (sub.Count == 0)
        {
            return before > 0 ? main with { AgentsBefore = before } : main;
        }

        return main with
        {
            Entries = ByTime(main.Entries, sub),
            Models = models,
            InputTokens = input,
            OutputTokens = output,
            AgentsBefore = before,
            Start = new[] { main.Start }.Concat(sub.Select(e => e.Time)).Where(t => t != null).Min(),
            End = new[] { main.End }.Concat(sub.Select(e => e.Time)).Where(t => t != null).Max()
        };
    }

    /// <summary>Both lists in time order; a step without a time stays after the step before it, and the main session goes first when the times are equal.</summary>
    internal static List<LogEntry> ByTime(List<LogEntry> main, List<LogEntry> sub)
    {
        static IEnumerable<(DateTime Time, LogEntry Entry)> Keyed(IEnumerable<LogEntry> entries)
        {
            var last = DateTime.MinValue;
            foreach (var e in entries)
            {
                last = e.Time ?? last;
                yield return (last, e);
            }
        }

        return [.. Keyed(main).Concat(Keyed(sub.GroupBy(e => e.Agent).SelectMany(g => g))).OrderBy(k => k.Time).Select(k => k.Entry)];
    }
}
