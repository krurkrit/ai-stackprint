// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Every step knows its line in the file, so the viewer can keep your place when it switches between Conversation and Raw.</summary>
public sealed class StepLinesTests
{
    private static IEnumerable<ReadOnlyMemory<byte>> Lines(params string[] lines) => lines.Select(l => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l));

    [Fact]
    public void A_session_step_has_the_number_of_its_line_counting_blank_and_unread_lines()
    {
        var s = new ClaudeSessionParser().Parse(Lines(
            """{"type":"summary","summary":"x"}""",
            "",
            """{"type":"user","timestamp":"2026-11-11T05:00:00Z","message":{"role":"user","content":"hello"}}""",
            "not json",
            """{"type":"assistant","timestamp":"2026-11-11T05:01:00Z","message":{"id":"m1","model":"m","content":[{"type":"text","text":"hi"},{"type":"text","text":"there"}]}}"""));
        Assert.Equal(3, s.Entries.First(e => e.Role == "user").Line);
        Assert.All(s.Entries.Where(e => e.Role == "assistant"), e => Assert.Equal(5, e.Line));
    }

    [Fact]
    public void A_text_log_step_has_the_line_where_it_starts()
    {
        var s = StepLinesLog.Parse();
        Assert.Equal([1L, 4L], s.Entries.Select(e => e.Line));
    }

    [Fact]
    public void A_step_made_outside_a_parse_has_no_line()
    {
        Assert.Equal(0, new LogEntry("user", "x", null, false).Line);
    }

    [Fact]
    public void A_reply_puts_its_tokens_on_its_first_step_once_even_when_streamed_on_several_lines()
    {
        var s = new ClaudeSessionParser().Parse(Lines(
            """{"type":"assistant","timestamp":"2026-11-11T05:00:00Z","requestId":"r1","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":900,"cache_creation_input_tokens":20},"content":[{"type":"text","text":"a"}]}}""",
            """{"type":"assistant","timestamp":"2026-11-11T05:00:01Z","requestId":"r1","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":900,"cache_creation_input_tokens":20},"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"ls"}}]}}""",
            """{"type":"user","timestamp":"2026-11-11T05:00:02Z","message":{"role":"user","content":"thanks"}}"""));
        Assert.Equal(3, s.Entries.Count);
        var usage = Assert.Single(s.Entries, e => e.Usage != null).Usage!;
        Assert.Equal("a", s.Entries[0].Text);
        Assert.Equal(("claude-opus-5-5", 10L, 5L, 900L, 20L), (usage.Model, usage.Input, usage.Output, usage.CacheRead, usage.CacheWrite));
        Assert.Null(s.Entries[2].Usage);   // a prompt has none
    }

    private static class StepLinesLog
    {
        public static Session Parse() => LineLogs.Parse(Lines(
            "2026-08-26 11:22:27 [info] Starting app {",
            "  appVersion: '1',",
            "}",
            "2026-08-26 11:22:28 [info] Done"));
    }
}
