// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>The Logs summary counts without building steps; its counts must equal those of the full parse.</summary>
public class SessionCountTests
{
    private static readonly string[] _claude =
    [
        """{"type":"summary","isSidechain":true,"summary":"earlier work"}""",
        """{"type":"user","timestamp":"2026-11-11T05:00:00Z","message":{"role":"user","content":"fix the build"}}""",
        """{"type":"assistant","timestamp":"2026-11-11T05:00:05Z","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":100,"output_tokens":20},"content":[{"type":"thinking","thinking":"look"},{"type":"text","text":"  "},{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet build"}}]}}""",
        """{"type":"assistant","timestamp":"2026-11-11T05:00:05Z","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":100,"output_tokens":20},"content":[{"type":"tool_use","id":"t2","name":"Read","input":{"file_path":"a.cs"}}]}}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"ok"},{"type":"text","text":"and the tests"}]}}""",
        """{"type":"assistant","isSidechain":true,"message":{"model":"<synthetic>","usage":{"input_tokens":5,"output_tokens":5},"content":[{"type":"text","text":"sub answer"},{"type":"tool_use","name":"Grep","input":{}}]}}""",
        """{"type":"user","isSidechain":true,"message":{"content":[{"type":"tool_result","content":"found"}]}}""",
        """{"type":"file-history-snapshot"}""",
        "not json",
        "   ",
    ];

    private static readonly string[] _codex =
    [
        """{"type":"turn_context","payload":{"model":"gpt-5-codex"}}""",
        """{"type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"<environment_context>cwd</environment_context>"}]}}""",
        """{"type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"rules"}]}}""",
        """{"type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"list files"}]}}""",
        """{"type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":" "}]}}""",
        """{"type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"ls\"]}"}}""",
        """{"type":"response_item","payload":{"type":"custom_tool_call","name":"apply_patch","input":"*** Begin Patch"}}""",
        """{"type":"response_item","payload":{"type":"function_call_output","output":"a.txt"}}""",
        """{"type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":80,"output_tokens":9}}}}""",
    ];

    [Theory]
    [InlineData("claude-session")]
    [InlineData("codex-session")]
    public void Count_gives_the_same_numbers_as_the_full_parse(string format)
    {
        var lines = format == "claude-session" ? _claude : _codex;
        var parser = LogParsers.Find(format)!;
        var full = SessionCounts.Of(parser.Parse(LogReader.Utf8(lines)));
        var fast = parser.Count(LogReader.Utf8(lines));

        Assert.Equal((full.Prompts, full.ToolCalls, full.SubAgentSteps, full.InputTokens, full.OutputTokens), (fast.Prompts, fast.ToolCalls, fast.SubAgentSteps, fast.InputTokens, fast.OutputTokens));
        Assert.Equal(full.Tools.OrderBy(t => t.Key), fast.Tools.OrderBy(t => t.Key));
        Assert.Equal(full.Models.Order(), fast.Models.Order());
        Assert.True(full.Prompts > 0 && full.ToolCalls > 1);   // the sample is not trivially empty
    }

    [Fact]
    public void Claude_counts_prompts_calls_and_sub_agent_steps()
    {
        var c = LogParsers.Find("claude-session")!.Count(LogReader.Utf8(_claude));
        Assert.Equal(2, c.Prompts);         // the string prompt and the text beside a tool result
        Assert.Equal(3, c.ToolCalls);
        Assert.Equal(4, c.SubAgentSteps);   // summary, answer, its call, its result
        Assert.Equal((105L, 25L), (c.InputTokens, c.OutputTokens));   // m1 once
        Assert.Equal(["claude-opus-5-5"], c.Models);
    }
}

public sealed class Utf8LinesTests : IDisposable
{
    private readonly string _file = Path.GetTempFileName();

    public void Dispose() => File.Delete(_file);

    private List<string> Read(long max) => [.. LogReader.Utf8Lines(_file, max).Select(m => Encoding.UTF8.GetString(m.Span))];

    [Fact]
    public void Lines_drop_the_byte_order_mark_and_line_endings_and_keep_a_last_line_without_a_break()
    {
        File.WriteAllBytes(_file, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("one\r\ntwo\n\nthree ü")]);
        Assert.Equal(["one", "two", "", "three ü"], Read(long.MaxValue));
    }

    [Fact]
    public void A_tail_starts_at_a_whole_line_and_lines_longer_than_the_buffer_come_whole()
    {
        var big = new string('x', 100_000);
        File.WriteAllText(_file, "first\n" + big + "\nlast\n");
        Assert.Equal(["first", big, "last"], Read(long.MaxValue));
        Assert.Equal(["last"], Read(8));   // "x…x\nlast\n": the cut line is dropped
        Assert.Equal(LogReader.Lines(_file, 8), Read(8));
    }
}
