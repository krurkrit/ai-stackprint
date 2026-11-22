// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class ConversationTests
{
    [Fact]
    public void Injected_blocks_are_split_from_the_text_around_them()
    {
        var parts = ContextBlocks.Split("Hello\n<INSTRUCTIONS>\nbe nice\n</INSTRUCTIONS>\nthen\n<!-- pstack:models:begin -->\nm1\n<!-- pstack:models:end -->");
        Assert.Equal([null, "INSTRUCTIONS", null, "pstack:models"], parts.Select(p => p.Label));
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
        Assert.Equal([MdKind.Heading, MdKind.Paragraph, MdKind.Bullet, MdKind.Bullet, MdKind.Number, MdKind.Quote, MdKind.Rule, MdKind.Table], b.Select(v => v.Kind));
        Assert.Equal(1, b[3].Level);
        Assert.Equal("1", b[4].Num);
        Assert.Equal(2, b[7].Rows!.Count);

        var s = MarkdownLite.Spans("a **bold** `code` *it* [x](http://u)");
        Assert.Contains(s, v => v.Bold && v.Text == "bold");
        Assert.Contains(s, v => v.Code && v.Text == "code");
        Assert.Contains(s, v => v.Italic && v.Text == "it");
        Assert.Contains(s, v => v.Url == "http://u" && v.Text == "x (http://u)");
    }

    [Fact]
    public void The_app_state_finds_a_definition_and_has_all_health_once_the_projects_are_checked()
    {
        var def = new AppDefinition { Id = "a" };
        var s = new AppState();
        s.Use(new Catalog([def], []));
        Assert.Same(def, s.Def("a"));
        Assert.Null(s.Def("b"));
        s.Health = [new HealthIssue("Skills", "x", "a", new Finding("one"))];
        Assert.False(s.HealthReady);
        Assert.Single(s.AllHealth);
        s.ProjectHealth = [new HealthIssue("Projects", "p", "a", new Finding("two"))];
        Assert.True(s.HealthReady);
        Assert.Equal(2, s.AllHealth.Count());
        Assert.Empty(s.Changes.Changes);
    }

    [Fact]
    public void Codex_limits_and_cache_writes_are_read()
    {
        var lines = new[]
        {
            """{"timestamp":"2026-11-11T05:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":40,"cache_write_input_tokens":10,"output_tokens":5,"reasoning_output_tokens":0}},"rate_limits":{"primary":{"used_percent":12.5,"window_minutes":300,"resets_at":1794000000},"secondary":{"used_percent":3,"window_minutes":10080,"resets_at":1794500000}}}}"""
        };
        var p = LogParsers.Find("codex-session")!;
        var l = p.Limits(LogReader.Utf8(lines)).ToList();
        Assert.Equal(["5-hour", "Weekly"], l.Select(v => v.Name));
        Assert.Equal(12.5, l[0].UsedPercent);
        var u = p.Usage(LogReader.Utf8(lines)).Single();
        Assert.Equal((50L, 40L, 10L), (u.Input, u.CacheRead, u.CacheWrite));
    }

    [Fact]
    public void An_empty_price_counts_as_no_price_and_zero_as_free()
    {
        var table = new ModelTable
        {
            Models =
            {
                ["m"] = new() { Price = new() },
                ["free"] = new()
                {
                    Price = new()
                    {
                        Input = 0,
                        Output = 0
                    }
                }
            }
        };
        Assert.Null(ModelCatalog.Cost("m-1", 100, 100, 0, 0, table));
        Assert.Equal(0m, ModelCatalog.Cost("free-1", 100, 100, 0, 0, table));
    }
}
