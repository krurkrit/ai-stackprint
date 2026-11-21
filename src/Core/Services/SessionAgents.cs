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

    /// <param name="cut">The session was read cut to its end: the agents' steps from before its first step are left out.</param>
    internal static Session Merge(Session main, string path, bool cut)
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
            foreach (var file in Directory.EnumerateFiles(dir, "agent-*.jsonl").Order(StringComparer.Ordinal).Take(MaxAgents))
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
