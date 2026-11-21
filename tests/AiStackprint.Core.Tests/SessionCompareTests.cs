// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Two sessions side by side: the summary figures and the lined-up steps.</summary>
public sealed class SessionCompareTests
{
    private static LogEntry Step(string role, string? tool = null, string text = "x") => new(role, text, null, false, tool);

    private static LogEntry Sub(string agent, string text = "look") => new("tool", text, null, true, "Grep", Agent: agent);

    private static Session Of(params LogEntry[] steps) => new([.. steps], [], 100, 20, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Local), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Local));

    [Fact]
    public void The_summary_marks_the_lower_figure_as_better_where_lower_is_better()
    {
        var a = Of(Step("user"), Step("tool", "Bash"), Step("assistant"));
        var b = Of(Step("user"), Step("tool", "Bash"), Step("tool", "Edit"), Step("tool", "Bash"), Step("assistant"));
        var rows = SessionCompare.Summary(a, b).ToDictionary(r => r.Label);
        Assert.Equal(("3", "5", -1), (rows["Steps"].A, rows["Steps"].B, rows["Steps"].Better));
        Assert.Equal(-1, rows["Tool calls"].Better);
        Assert.Equal(0, rows["Prompts"].Better);   // equal, and more prompts is not better or worse
        Assert.Equal("Bash 1", rows["Tools used"].A);
        Assert.Equal("Bash 2 · Edit 1", rows["Tools used"].B);
        Assert.Equal("5 min", rows["Duration"].A);
        Assert.Equal(0, rows["Duration"].Better);
    }

    [Fact]
    public void Steps_with_the_same_role_and_tool_are_matched_in_order_and_the_rest_are_gaps()
    {
        var a = Of(Step("user"), Step("tool", "Read"), Step("assistant"));
        var b = Of(Step("user"), Step("tool", "Read"), Step("tool", "Grep"), Step("assistant"));
        var pairs = SessionCompare.Align(a, b);
        Assert.Equal([true, true, false, true], pairs.Select(p => p.Matched));
        Assert.Null(pairs[2].A);   // a gap where the first session has nothing
        Assert.Equal("Grep", pairs[2].B!.Step!.Tool);
    }

    [Fact]
    public void A_sub_agent_is_one_block_with_its_steps_counted_and_blocks_match_each_other()
    {
        var a = Of(Step("user"), Step("assistant"), Sub("a1"), Sub("a1"), Step("assistant"));
        var b = Of(Step("user"), Step("assistant"), Sub("b1"), Sub("b1"), Sub("b1"), Step("assistant"));
        var pairs = SessionCompare.Align(a, b);
        Assert.Equal(4, pairs.Count);
        var block = pairs[2];
        Assert.True(block.Matched);
        Assert.Equal((2, 3), (block.A!.Steps, block.B!.Steps));
        Assert.StartsWith("Agent 1", block.A.Label, StringComparison.Ordinal);
        var rows = SessionCompare.Summary(a, b).ToDictionary(r => r.Label);
        Assert.Equal(("1", "1"), (rows["Sub-agents"].A, rows["Sub-agents"].B));
    }
}
