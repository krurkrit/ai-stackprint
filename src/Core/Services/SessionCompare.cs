// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace AiStackprint.Services;

/// <param name="Better">-1: the first session has the lower (better) value, 1: the second, 0: neither (equal, or a figure with no better side).</param>
public sealed record CompareRow(string Label, string A, string B, int Better);

/// <summary>A main step, or all the steps of one sub-agent shown as a block.</summary>
/// <param name="Step">The main step; null for a sub-agent block.</param>
/// <param name="Label">A sub-agent block: its name and what it was asked, on one line.</param>
public sealed record CompareItem(LogEntry? Step, string Label, int Steps, decimal Cost, string Key);

/// <summary>One line of the two sessions side by side: a step on one side and its match on the other, or a gap where the other session has nothing.</summary>
public sealed record StepPair(CompareItem? A, CompareItem? B)
{
    public bool Matched => A != null && B != null;
}

/// <summary>Two sessions side by side: the figures that differ, and their steps lined up (an approximate match by who did what).</summary>
public static class SessionCompare
{
    private const int MaxItems = 600;   // the matching is quadratic in the steps of the two sessions

    public static List<CompareRow> Summary(Session a, Session b)
    {
        var (x, y) = (Stats(a), Stats(b));
        var rows = new List<CompareRow>
        {
            Row("Steps", x.Steps, y.Steps, N),
            Row("Prompts", x.Prompts, y.Prompts, N, false),
            Row("Tool calls", x.Tools, y.Tools, N),
            Row("Sub-agents", x.Agents, y.Agents, N, false),
            new("Tokens in / out", $"{Short(a.InputTokens)} / {Short(a.OutputTokens)}", $"{Short(b.InputTokens)} / {Short(b.OutputTokens)}", Cmp(a.InputTokens + a.OutputTokens, b.InputTokens + b.OutputTokens)),
            new("Estimated cost", Money(x.Cost), Money(y.Cost), x.HasCost && y.HasCost ? Cmp((double)x.Cost, (double)y.Cost) : 0),
            new("Main: steps · cost", $"{x.MainSteps} · {Money(x.MainCost)}", $"{y.MainSteps} · {Money(y.MainCost)}", 0),
            new("Sub-agents: steps · cost", $"{x.SubSteps} · {Money(x.SubCost)}", $"{y.SubSteps} · {Money(y.SubCost)}", 0),
            Row("Files changed", x.Files, y.Files, N, false),
            new("Duration", Duration(a), Duration(b), a.Start != null && a.End != null && b.Start != null && b.End != null ? Cmp((a.End - a.Start).Value.TotalSeconds, (b.End - b.Start).Value.TotalSeconds) : 0),
            new("Tools used", x.ToolList, y.ToolList, 0)
        };
        return rows;
    }

    /// <summary>The two sessions' steps lined up: steps with the same role and tool are matched in order; sub-agents are blocks that match each other. Longer sessions use their last <see cref="MaxItems"/> items.</summary>
    public static List<StepPair> Align(Session a, Session b)
    {
        var (x, y) = (Items(a), Items(b));
        var n = x.Count;
        var m = y.Count;
        var best = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                best[i, j] = x[i].Key == y[j].Key ? best[i + 1, j + 1] + 1 : Math.Max(best[i + 1, j], best[i, j + 1]);
            }
        }

        var pairs = new List<StepPair>();
        int p = 0, q = 0;
        while (p < n || q < m)
        {
            if (p < n && q < m && x[p].Key == y[q].Key)
            {
                pairs.Add(new(x[p++], y[q++]));
            }
            else if (q >= m || (p < n && best[p + 1, q] >= best[p, q + 1]))
            {
                pairs.Add(new(x[p++], null));
            }
            else
            {
                pairs.Add(new(null, y[q++]));
            }
        }

        return pairs;
    }

    /// <summary>The steps of a session as items: main steps one each, and each sub-agent as one block where its first step is.</summary>
    internal static List<CompareItem> Items(Session s)
    {
        var graph = SessionGraph.Layout(s.Entries);
        var items = new List<CompareItem>();
        var block = new Dictionary<int, int>();   // agent number -> index in items
        for (var i = 0; i < s.Entries.Count; i++)
        {
            var e = s.Entries[i];
            if (graph?.Rows[i].Owner.Agent is > 0 and var agent)
            {
                var cost = StepCost(e);
                if (block.TryGetValue(agent, out var at))
                {
                    items[at] = items[at] with { Steps = items[at].Steps + 1, Cost = items[at].Cost + cost };
                }
                else
                {
                    block[agent] = items.Count;
                    var task = e.Text.Trim().Split('\n')[0];
                    items.Add(new(null, graph.Agents[agent] + " · " + (task.Length > 60 ? task[..60] + "…" : task), 1, cost, "agent"));
                }
            }
            else
            {
                items.Add(new(e, "", 1, StepCost(e), e.Role + "|" + e.Tool));
            }
        }

        return items.Count > MaxItems ? items[^MaxItems..] : items;
    }

    private static decimal StepCost(LogEntry e) => e.Usage != null ? ModelCatalog.Cost(e.Usage) ?? 0 : 0;

    private sealed record Figures(int Steps, int Prompts, int Tools, int Agents, int Files, int MainSteps, int SubSteps, decimal Cost, decimal MainCost, decimal SubCost, bool HasCost, string ToolList);

    private static Figures Stats(Session s)
    {
        var graph = SessionGraph.Layout(s.Entries);
        decimal main = 0, sub = 0;
        var hasCost = false;
        int mainSteps = 0, subSteps = 0;
        for (var i = 0; i < s.Entries.Count; i++)
        {
            var e = s.Entries[i];
            hasCost |= e.Usage != null;
            var isSub = graph?.Rows[i].Owner.Agent is > 0;
            if (isSub)
            {
                subSteps++;
                sub += StepCost(e);
            }
            else
            {
                mainSteps++;
                main += StepCost(e);
            }
        }

        var tools = s.Entries.Where(e => e.Role == "tool" && e.Tool != null).GroupBy(e => e.Tool!).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Key} {g.Count()}");
        return new(s.Entries.Count, s.Entries.Count(e => e.Role == "user"), s.Entries.Count(e => e.Role == "tool"), Math.Max(0, (graph?.Agents.Length ?? 1) - 1),
            SessionExport.TouchedFiles(s.Entries).Count, mainSteps, subSteps, main + sub, main, sub, hasCost, string.Join(" · ", tools));
    }

    private static CompareRow Row(string label, int a, int b, Func<int, string> format, bool lowerIsBetter) =>
        new(label, format(a), format(b), lowerIsBetter ? Cmp(a, b) : 0);

    private static CompareRow Row(string label, int a, int b, Func<int, string> format) => Row(label, a, b, format, true);

    private static int Cmp(double a, double b) => Math.Abs(a - b) < 1e-9 ? 0 : a < b ? -1 : 1;

    private static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    private static string Short(long n) => n switch
    {
        >= 1_000_000 => (n / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (n / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K",
        _ => n.ToString(CultureInfo.InvariantCulture)
    };

    private static string Money(decimal d) => d == 0 ? "–" : "$" + d.ToString(d < 0.1m ? "0.0000" : d < 100 ? "N2" : "N0", CultureInfo.InvariantCulture);

    private static string Duration(Session s) => s.Start is { } a && s.End is { } b
        ? (b - a).TotalMinutes < 1 ? $"{(b - a).TotalSeconds:0} s" : $"{(b - a).TotalMinutes:0} min"
        : "–";
}
