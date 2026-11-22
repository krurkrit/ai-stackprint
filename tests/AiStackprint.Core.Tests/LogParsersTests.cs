// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class LogParsersTests
{
    [Fact]
    public void Claude_session_becomes_steps_with_tools_subagents_and_tokens()
    {
        var lines = new[]
        {
            """{"type":"user","timestamp":"2026-11-11T05:00:00Z","message":{"role":"user","content":"fix the build"}}""",
            """{"type":"assistant","timestamp":"2026-11-11T05:00:05Z","message":{"model":"claude-opus-5-5","usage":{"input_tokens":100,"output_tokens":20},"content":[{"type":"thinking","thinking":"look at errors"},{"type":"text","text":"Running the build."},{"type":"tool_use","name":"Bash","input":{"command":"dotnet build"}}]}}""",
            """{"type":"user","timestamp":"2026-11-11T05:00:09Z","message":{"role":"user","content":[{"type":"tool_result","content":[{"type":"text","text":"Build succeeded."}]}]}}""",
            """{"type":"assistant","isSidechain":true,"timestamp":"2026-11-11T05:00:10Z","message":{"usage":{"input_tokens":5,"output_tokens":5},"content":[{"type":"text","text":"sub-agent answer"}]}}""",
            """{"type":"file-history-snapshot"}""",
            "not json"
        };
        var s = LogParsers.Find("claude-session")!.Parse(LogReader.Utf8(lines));
        Assert.Equal(["user", "thinking", "assistant", "tool", "result", "assistant"], s.Entries.Select(e => e.Role));
        Assert.Equal("Bash", s.Entries[3].Tool);
        Assert.Equal("dotnet build", s.Entries[3].Text);
        Assert.Equal("Build succeeded.", s.Entries[4].Text);
        Assert.True(s.Entries[5].SubAgent);
        Assert.Equal(105, s.InputTokens);
        Assert.Equal(25, s.OutputTokens);
        Assert.Equal(new[] { "claude-opus-5-5" }, s.Models);
    }

    [Fact]
    public void Codex_session_maps_messages_calls_outputs_and_cumulative_tokens()
    {
        var lines = new[]
        {
            """{"timestamp":"2026-11-11T05:00:00Z","type":"turn_context","payload":{"model":"gpt-5-codex"}}""",
            """{"timestamp":"2026-11-11T05:00:00Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"<environment_context>cwd</environment_context>"}]}}""",
            """{"timestamp":"2026-11-11T05:00:01Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"list files"}]}}""",
            """{"timestamp":"2026-11-11T05:00:02Z","type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"bash\",\"-lc\",\"ls\"]}"}}""",
            """{"timestamp":"2026-11-11T05:00:03Z","type":"response_item","payload":{"type":"function_call_output","output":"a.txt"}}""",
            """{"timestamp":"2026-11-11T05:00:04Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":50,"output_tokens":7}}}}""",
            """{"timestamp":"2026-11-11T05:00:05Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":80,"output_tokens":9}}}}""",
            """{"timestamp":"2026-11-11T05:00:06Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"One file."}]}}"""
        };
        var s = LogParsers.Find("codex-session")!.Parse(LogReader.Utf8(lines));
        Assert.Equal(["system", "user", "tool", "result", "assistant"], s.Entries.Select(e => e.Role));
        Assert.Equal("bash -lc ls", s.Entries[2].Text);
        Assert.Equal(80, s.InputTokens);
        Assert.Equal(9, s.OutputTokens);
        Assert.Equal(new[] { "gpt-5-codex" }, s.Models);
        Assert.Null(LogParsers.Find("unknown"));
    }
}

public class LogParserHintsTests
{
    [Fact]
    public void Tool_steps_carry_full_input_and_highlighting_hints()
    {
        var lines = new[]
        {
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"src/App.cs"}},{"type":"tool_use","id":"t2","name":"Bash","input":{"command":"dotnet build\necho done"}}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"class App {}"},{"type":"tool_result","tool_use_id":"t2","content":"{\"ok\":true}"}]}}"""
        };
        var e = LogParsers.Find("claude-session")!.Parse(LogReader.Utf8(lines)).Entries;
        Assert.Equal(".json", e[0].Lang);
        Assert.Contains("src/App.cs", e[0].Detail!);
        Assert.Equal(".sh", e[1].Lang);
        Assert.Equal("dotnet build\necho done", e[1].Detail);
        Assert.Equal(".cs", e[2].Lang);       // Read result highlighted as the file it read
        Assert.Equal("Read", e[2].Tool);
        Assert.Equal(".json", e[3].Lang);     // JSON-looking output
    }

    private static System.Text.Json.JsonElement Json(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void A_tool_input_given_as_a_string_is_read_as_json_when_it_is_and_kept_as_text_when_not()
    {
        Assert.Equal("ls -la", LogParsers.Summary(Json("\"{\\\"command\\\":\\\"ls -la\\\"}\"")));
        Assert.Equal("plain words", LogParsers.Summary(Json("\"plain words\"")));
        var call = LogParsers.ToolCall("apply", Json("\"not json {\""), null, false);
        Assert.Equal((".txt", "not json {"), (call.Lang, call.Detail));
        Assert.Equal("ls", LogParsers.ToolCall("Bash", Json("\"{\\\"command\\\":\\\"ls\\\"}\""), null, false).Text);
    }

    [Fact]
    public void Output_types_are_detected_from_the_text()
    {
        Assert.Equal(".cs", LogParsers.Detect("{}", ".cs"));
        Assert.Equal(".diff", LogParsers.Detect("  diff --git a/x b/x", null));
        Assert.Equal(".diff", LogParsers.Detect("*** Begin Patch", null));
        Assert.Equal(".json", LogParsers.Detect("[1, 2]", null));
        Assert.Null(LogParsers.Detect("{ not json", null));
        Assert.Null(LogParsers.Detect("words", null));
        Assert.Equal("a\nb", LogParsers.Text(Json("""["a", {"type":"text","text":"b"}, {"type":"image"}]""")));
        Assert.Equal("", LogParsers.Text(Json("42")));
    }

    [Fact]
    public void A_format_without_its_own_count_feed_or_usage_uses_the_defaults()
    {
        ILogParser p = new MinimalParser();
        Assert.Equal(1, p.Count(LogReader.Utf8(["""{"type":"user","message":{"content":"hi"}}"""])).Prompts);
        Assert.Null(p.Feed());
        Assert.Empty(p.Usage([]));
        Assert.Empty(p.Limits([]));
        Assert.False(p.WholeFile);
    }

    private sealed class MinimalParser : ILogParser
    {
        public string Format => "minimal";
        public Session Parse(IEnumerable<ReadOnlyMemory<byte>> lines) => new([.. lines.Select(_ => new LogEntry("user", "hi", null, false))], [], 0, 0, null, null);
    }
}
