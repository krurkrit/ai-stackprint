// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>The graph beside the conversation: where each sub-agent branches off, runs and rejoins, and which lane it is drawn in.</summary>
public sealed class SessionGraphTests
{
    private static LogEntry Main(string text = "m") => new("assistant", text, null, false);

    private static LogEntry Sub(string? agent) => new("tool", "s", null, true, Agent: agent);

    [Fact]
    public void A_session_without_sub_agents_has_no_graph()
    {
        Assert.Null(SessionGraph.Layout([Main(), Main()]));
    }

    [Fact]
    public void An_agent_branches_off_the_step_before_and_rejoins_at_the_step_after()
    {
        var g = SessionGraph.Layout([Main(), Main(), Sub("a"), Sub("a"), Sub("a"), Main(), Main()])!;
        Assert.Equal(2, g.Lanes);
        Assert.Equal([new Branch(1, 1)], g.Rows[1].Forks);
        Assert.Equal([new Branch(1, 1)], g.Rows[5].Joins);
        Assert.Equal(new Branch(1, 1), g.Rows[3].Owner);
        Assert.Equal(default, g.Rows[0].Owner);   // main
        Assert.Empty(g.Rows[0].Open);
        Assert.Equal([new Branch(1, 1)], g.Rows[4].Open);
        Assert.Empty(g.Rows[6].Open);
    }

    [Fact]
    public void Agents_running_together_get_lanes_side_by_side_and_a_lane_is_used_again_after_it_rejoins()
    {
        var g = SessionGraph.Layout([
            Main(), Sub("a"), Sub("a"), Main(),                    // agent 1 alone
            Main(), Sub("b"), Sub("c"), Sub("b"), Sub("c"), Main(), // agents 2 and 3 at the same time
            Main()])!;
        Assert.Equal(3, g.Lanes);
        Assert.Equal(1, g.Rows[1].Owner.Lane);
        Assert.Equal(1, g.Rows[5].Owner.Lane);   // agent 2 reuses lane 1
        Assert.Equal(2, g.Rows[6].Owner.Lane);   // agent 3 beside it
        Assert.Equal([new Branch(1, 2), new Branch(2, 3)], g.Rows[7].Open);   // while one of them has a step, the other shows only its line
        Assert.Equal(new Branch(1, 2), g.Rows[7].Owner);
    }

    [Fact]
    public void Steps_without_agent_ids_make_one_agent_per_unbroken_run()
    {
        var g = SessionGraph.Layout([Main(), Sub(null), Sub(null), Main(), Sub(null), Main()])!;
        Assert.Equal(["Main", "Agent 1", "Agent 2"], g.Agents);
        Assert.Equal(1, g.Lanes - 1);
    }

    [Fact]
    public void An_agent_still_running_at_the_end_has_no_join_and_one_with_an_id_shows_it()
    {
        var g = SessionGraph.Layout([Main(), Sub("abcdef123456")])!;
        Assert.Empty(g.Rows.SelectMany(r => r.Joins));
        Assert.Equal("Agent 1 · abcdef1", g.Agents[1]);
    }
}
