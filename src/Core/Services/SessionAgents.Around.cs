// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>The part of a long session around its sub-agents, read as the steps come; when each agent ran, cached per agent file.</summary>
public static partial class SessionAgents
{
    /// <summary>
    /// The main steps from <see cref="ContextSteps"/> before the first that starts at or after <paramref name="from"/> to as many after the last at or before
    /// <paramref name="to"/>; the last steps when none starts that late. Read as the steps come, keeping only those (the session's times only grow, so the
    /// read stops once enough steps after <paramref name="to"/> are in).
    /// </summary>
    internal static List<LogEntry> Around(IEnumerable<LogEntry> steps, DateTime from, DateTime to)
    {
        var before = new Queue<LogEntry>();   // the last steps before the first in the stretch
        List<LogEntry>? part = null;
        var lastIn = -1;
        foreach (var e in steps)
        {
            if (part == null)
            {
                if (e.Time >= from)
                {
                    part = [.. before.Skip(Math.Max(0, before.Count - ContextSteps)), e];
                    lastIn = part.Count - 1;
                    continue;
                }

                before.Enqueue(e);
                if (before.Count > ContextSteps + 1)
                {
                    before.Dequeue();
                }

                continue;
            }

            part.Add(e);
            if (e.Time <= to)
            {
                lastIn = part.Count - 1;
            }
            else if (e.Time != null && part.Count - 1 - lastIn >= ContextSteps)
            {
                break;
            }
        }

        return part == null ? [.. before] : [.. part.Take(lastIn + ContextSteps + 1)];
    }

    internal sealed record AgentSpan(DateTime? First, DateTime? Last);

    private static readonly FileMemo<AgentSpan> _spans = new("agent-times");

    /// <summary>When an agent file's steps start and end: read once per version of the file (kept in <c>cache/agent-times.json</c>).</summary>
    private static AgentSpan Span(string file, ILogParser parser)
    {
        var info = new FileInfo(file);
        var key = new LogFile("", "", file, info.Length, info.LastWriteTime, null, "claude-session");
        return _spans.GetOrAdd(key, LogSummary.Build, () =>
        {
            DateTime? first = null, last = null;
            foreach (var t in parser.Parse(LogReader.Utf8Lines(file, AgentBytes)).Entries.Select(e => e.Time).Where(t => t != null))
            {
                first = first == null || t < first ? t : first;
                last = last == null || t > last ? t : last;
            }

            return new AgentSpan(first, last);
        });
    }
}
