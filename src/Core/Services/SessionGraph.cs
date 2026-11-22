// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Services;

/// <summary>One sub-agent's branch: the lane it is drawn in (1 and up; 0 is the main session) and its number (1 and up, in the order the agents start; 0 = main).</summary>
public readonly record struct Branch(int Lane, int Agent);

/// <param name="Owner">Who made the step: main (lane 0, agent 0) or a sub-agent's branch. Its dot is drawn in that lane.</param>
/// <param name="Open">Branches that are open on this row (a line runs through it): the owner's own and every other running agent's; a lane that is not the step's own shows only the line, no dot.</param>
/// <param name="Forks">Branches that leave the main line on this row (the agent is started here).</param>
/// <param name="Joins">Branches that rejoin the main line on this row (the main session picks up the result here).</param>
public sealed record GraphRow(Branch Owner, Branch[] Open, Branch[] Forks, Branch[] Joins);

/// <param name="Lanes">Lanes drawn, the main one included.</param>
/// <param name="Agents">A label per agent number (index 0 = main): "Agent 2", with the agent's id when the log has one.</param>
public sealed record SessionGraph(GraphRow[] Rows, int Lanes, string[] Agents)
{
    /// <summary>
    /// Lays the steps out like <c>git log --graph</c>: the main session is a line; each sub-agent branches off the main step before its first step and
    /// rejoins at the first main step after its last one. Agents that run at the same time get lanes side by side; a lane is used again once its agent has rejoined.
    /// Null when no step is a sub-agent's. Without agent ids, each unbroken run of sub-agent steps is one agent.
    /// </summary>
    public static SessionGraph? Layout(IReadOnlyList<LogEntry> steps)
    {
        var groups = new List<(string Key, int First, int Last)>();
        var byKey = new Dictionary<string, int>();
        var run = 0;
        var prevSub = false;
        var keys = new string?[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var e = steps[i];
            if (e.SubAgent)
            {
                run += prevSub ? 0 : 1;
                var key = string.IsNullOrEmpty(e.Agent) ? "run:" + run : "id:" + e.Agent;
                keys[i] = key;
                if (byKey.TryGetValue(key, out var g))
                {
                    groups[g] = groups[g] with { Last = i };
                }
                else
                {
                    byKey[key] = groups.Count;
                    groups.Add((key, i, i));
                }
            }

            prevSub = e.SubAgent;
        }

        if (groups.Count == 0)
        {
            return null;
        }

        // fork: the main step before the first step (-1: none); merge: the main step after the last (MaxValue: still running at the end)
        var spans = new (int Fork, int Merge)[groups.Count];
        var lanes = new int[groups.Count];
        var freeAt = new List<int>();   // per lane (index 0 = lane 1): the row where its last agent rejoined
        for (var g = 0; g < groups.Count; g++)
        {
            var fork = -1;
            for (var j = groups[g].First - 1; j >= 0; j--)
            {
                if (!steps[j].SubAgent)
                {
                    fork = j;
                    break;
                }
            }

            var merge = int.MaxValue;
            for (var j = groups[g].Last + 1; j < steps.Count; j++)
            {
                if (!steps[j].SubAgent)
                {
                    merge = j;
                    break;
                }
            }

            spans[g] = (fork, merge);
            var lane = freeAt.FindIndex(f => f <= fork);
            if (lane < 0)
            {
                freeAt.Add(merge);
                lane = freeAt.Count - 1;
            }
            else
            {
                freeAt[lane] = merge;
            }

            lanes[g] = lane + 1;
        }

        var rows = new GraphRow[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var open = new List<Branch>();
            var forks = new List<Branch>();
            var joins = new List<Branch>();
            for (var g = 0; g < groups.Count; g++)
            {
                var b = new Branch(lanes[g], g + 1);
                if (spans[g].Fork < i && i < spans[g].Merge)
                {
                    open.Add(b);
                }

                if (spans[g].Fork == i)
                {
                    forks.Add(b);
                }

                if (spans[g].Merge == i)
                {
                    joins.Add(b);
                }
            }

            var owner = keys[i] is { } k ? new Branch(lanes[byKey[k]], byKey[k] + 1) : default;
            rows[i] = new GraphRow(owner, [.. open], [.. forks], [.. joins]);
        }

        var names = new string[groups.Count + 1];
        names[0] = "Main";
        for (var g = 0; g < groups.Count; g++)
        {
            names[g + 1] = $"Agent {g + 1}" + (groups[g].Key.StartsWith("id:", StringComparison.Ordinal) ? " · " + groups[g].Key[3..Math.Min(groups[g].Key.Length, 10)] : "");
        }

        return new SessionGraph(rows, freeAt.Count + 1, names);
    }
}
