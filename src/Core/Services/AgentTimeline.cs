// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Services;

/// <param name="Fork">When the main session started it (its Task/Agent call, else the step before).</param>
/// <param name="Merge">When the main session picked the result up (the first main step after the lane ended).</param>
public record AgentLane(string Label, DateTime Start, DateTime End, int Steps, DateTime Fork, DateTime Merge);

/// <param name="Waits">Periods where at least one sub-agent ran (the main session waits or works alongside).</param>
public record AgentTimeline(DateTime Start, DateTime End, List<AgentLane> Lanes, List<(DateTime From, DateTime To)> Waits, int TotalLanes);

/// <summary>When sub-agents branched off the main session, ran (also in parallel) and came back. Null when the session has none.</summary>
public static class AgentTimelines
{
    internal const int MaxLanes = 30;

    public static AgentTimeline? Build(IReadOnlyList<LogEntry> entries)
    {
        var timed = entries.Where(e => e.Time != null).ToList();
        if (!timed.Any(e => e.SubAgent))
        {
            return null;
        }

        // One lane per agent id; without ids, one lane per unbroken run of sub-agent steps
        var lanes = new List<List<LogEntry>>();
        var byAgent = new Dictionary<string, int>();
        var run = -1;
        var prevSub = false;
        foreach (var e in timed)
        {
            if (!e.SubAgent)
            {
                prevSub = false;
                continue;
            }
            int idx;
            if (!string.IsNullOrEmpty(e.Agent))
            {
                if (!byAgent.TryGetValue(e.Agent, out idx))
                {
                    idx = lanes.Count;
                    byAgent[e.Agent] = idx;
                    lanes.Add([]);
                }
            }
            else
            {
                if (!prevSub || run < 0)
                {
                    run = lanes.Count;
                    lanes.Add([]);
                }
                idx = run;
            }
            lanes[idx].Add(e);
            prevSub = true;
        }

        var main = timed.Where(e => !e.SubAgent).ToList();
        var tasks = main.Where(e => e.Role == "tool" && e.Tool is "Task" or "Agent").ToList();
        var used = new HashSet<LogEntry>();
        var result = new List<AgentLane>();
        var n = 0;
        foreach (var steps in lanes.OrderBy(l => l.Min(e => e.Time)))
        {
            DateTime start = steps.Min(e => e.Time)!.Value, end = steps.Max(e => e.Time)!.Value;
            var call = tasks.LastOrDefault(t => t.Time <= start && !used.Contains(t));
            if (call != null)
            {
                used.Add(call);
            }

            var fork = call?.Time ?? main.LastOrDefault(e => e.Time <= start)?.Time ?? start;
            var merge = main.FirstOrDefault(e => e.Time >= end)?.Time ?? end;
            var first = steps[0].Text.Trim().Split('\n')[0];
            result.Add(new($"#{++n} {(first.Length > 34 ? first[..34] + "…" : first)}".TrimEnd(), start, end, steps.Count, fork, merge));
        }

        var waits = new List<(DateTime From, DateTime To)>();
        foreach (var l in result.OrderBy(l => l.Start))
        {
            if (waits.Count > 0 && l.Start <= waits[^1].To)
            {
                waits[^1] = (waits[^1].From, l.End > waits[^1].To ? l.End : waits[^1].To);
            }
            else
            {
                waits.Add((l.Start, l.End));
            }
        }
        return new(timed[0].Time!.Value, timed[^1].Time!.Value, [.. result.Take(MaxLanes)], waits, result.Count);
    }
}
