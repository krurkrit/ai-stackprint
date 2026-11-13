// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class ConversationTests
{
    [Fact]
    public void Injected_blocks_are_split_from_the_text_around_them()
    {
        var parts = ContextBlocks.Split("Hello\n<INSTRUCTIONS>\nbe nice\n</INSTRUCTIONS>\nthen\n<!-- pstack:models:begin -->\nm1\n<!-- pstack:models:end -->");
        Assert.Equal(new string?[] { null, "INSTRUCTIONS", null, "pstack:models" }, parts.Select(p => p.Label));
        Assert.Equal("be nice", parts[1].Text);
        Assert.Equal("m1", parts[3].Text);
    }

    [Fact]
    public void Ordinary_html_and_unclosed_tags_stay_text()
    {
        var parts = ContextBlocks.Split("use <div>x</div> and <INSTRUCTIONS> without end");
        Assert.Single(parts);
        Assert.Null(parts[0].Label);
    }

    [Fact]
    public void Markdown_blocks_and_spans_are_read()
    {
        var b = MarkdownLite.Blocks("# Title\n\ntext line\nmore\n\n- one\n  - nested\n1. first\n\n> quote\n\n---\n\n| a | b |\n|---|---|\n| 1 | 2 |");
        Assert.Equal(new[] { MdKind.Heading, MdKind.Paragraph, MdKind.Bullet, MdKind.Bullet, MdKind.Number, MdKind.Quote, MdKind.Rule, MdKind.Table }, b.Select(v => v.Kind));
        Assert.Equal(1, b[3].Level);
        Assert.Equal("1", b[4].Num);
        Assert.Equal(2, b[7].Rows!.Count);

        var s = MarkdownLite.Spans("a **bold** `code` *it* [x](http://u)");
        Assert.Contains(s, v => v.Bold && v.Text == "bold");
        Assert.Contains(s, v => v.Code && v.Text == "code");
        Assert.Contains(s, v => v.Italic && v.Text == "it");
        Assert.Contains(s, v => v.Url == "http://u" && v.Text == "x (http://u)");
    }

    static DateTime T(int s) => new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Local).AddSeconds(s);

    [Fact]
    public void Agent_timeline_gives_each_agent_a_lane_with_fork_and_merge()
    {
        var e = new List<LogEntry>
        {
            new("user", "go", T(0), false),
            new("tool", "{}", T(5), false, "Task"),
            new("assistant", "first job", T(10), true, Agent: "a1"),
            new("assistant", "second job", T(12), true, Agent: "a2"),
            new("assistant", "done", T(30), true, Agent: "a1"),
            new("assistant", "done", T(40), true, Agent: "a2"),
            new("assistant", "merged", T(50), false)
        };
        var t = AgentTimelines.Build(e)!;
        Assert.Equal(2, t.Lanes.Count);
        var l0 = t.Lanes[0];
        Assert.Equal((T(5), T(10), T(30), T(50)), (l0.Fork, l0.Start, l0.End, l0.Merge));
        Assert.Equal(new[] { (T(10), T(40)) }, t.Waits);
        Assert.Null(AgentTimelines.Build(e.Where(v => !v.SubAgent).ToList()));
    }

    [Fact]
    public void Codex_limits_and_cache_writes_are_read()
    {
        var lines = new[]
        {
            """{"timestamp":"2026-11-11T05:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":40,"cache_write_input_tokens":10,"output_tokens":5,"reasoning_output_tokens":0}},"rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300,"resets_at":1794000000},"secondary":{"used_percent":3,"window_minutes":10080,"resets_at":1794500000}}}}"""
        };
        var p = LogParsers.Find("codex-session")!;
        var l = p.Limits(lines).ToList();
        Assert.Equal(new[] { "5-hour", "Weekly" }, l.Select(v => v.Name));
        Assert.Equal(12.5, l[0].UsedPercent);
        var u = p.Usage(lines).Single();
        Assert.Equal((50L, 40L, 10L), (u.Input, u.CacheRead, u.CacheWrite));
    }

    [Fact]
    public void An_all_zero_price_counts_as_no_price()
    {
        var table = new PriceFile { Models = { ["m"] = new ModelPrice() } };
        Assert.Null(Pricing.Cost("m-1", 100, 100, 0, 0, table));
    }
}
