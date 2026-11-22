// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Edits as diffs, the files a session changed, a session or a step as Markdown or HTML, and Codex tokens per reply.</summary>
public sealed class SessionExportTests
{
    private static IEnumerable<ReadOnlyMemory<byte>> Lines(params string[] lines) => lines.Select(l => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l));

    private static Session Claude(params string[] tools) => new ClaudeSessionParser().Parse(Lines(tools.Select((t, i) =>
        "{\"type\":\"assistant\",\"timestamp\":\"2026-11-11T05:0" + i + ":00Z\",\"message\":{\"id\":\"m" + i + "\",\"model\":\"m\",\"content\":[" + t + "]}}").ToArray()));

    [Fact]
    public void Edit_Write_and_MultiEdit_calls_are_diffs()
    {
        var s = Claude(
            """{"type":"tool_use","id":"a","name":"Edit","input":{"file_path":"src/A.cs","old_string":"int x = 1;","new_string":"int x = 2;\nint y = 3;"}}""",
            """{"type":"tool_use","id":"b","name":"Write","input":{"file_path":"notes.md","content":"hello"}}""",
            """{"type":"tool_use","id":"c","name":"MultiEdit","input":{"file_path":"src/B.cs","edits":[{"old_string":"a","new_string":"b"},{"old_string":"c","new_string":"d"}]}}""");
        Assert.All(s.Entries, e => Assert.Equal(".diff", e.Lang));
        Assert.Equal("--- src/A.cs\n+++ src/A.cs\n@@\n-int x = 1;\n+int x = 2;\n+int y = 3;\n", s.Entries[0].Detail);
        Assert.Contains("+hello", s.Entries[1].Detail);
        Assert.Equal(2, s.Entries[2].Detail!.Split("@@").Length - 1);
    }

    [Fact]
    public void Other_tool_calls_stay_as_they_were()
    {
        var s = Claude("""{"type":"tool_use","id":"a","name":"Bash","input":{"command":"ls"}}""");
        Assert.Equal(".sh", s.Entries[0].Lang);
    }

    [Fact]
    public void Touched_files_come_from_diffs_and_patches_once_each_in_order()
    {
        var s = Claude(
            """{"type":"tool_use","id":"a","name":"Edit","input":{"file_path":"b.cs","old_string":"1","new_string":"2"}}""",
            """{"type":"tool_use","id":"b","name":"Edit","input":{"file_path":"a.cs","old_string":"1","new_string":"2"}}""",
            """{"type":"tool_use","id":"c","name":"Edit","input":{"file_path":"b.cs","old_string":"2","new_string":"3"}}""");
        var entries = s.Entries.Append(new LogEntry("tool", "apply_patch", null, false, "apply_patch", ".diff", "*** Begin Patch\n*** Update File: c.cs\n@@\n-x\n+y\n*** Add File: d.cs\n+z\n*** End Patch")).ToList();
        Assert.Equal(["b.cs", "a.cs", "c.cs", "d.cs"], SessionExport.TouchedFiles(entries));
    }

    [Fact]
    public void A_step_is_Markdown_with_a_fence_longer_than_any_run_of_backticks_inside()
    {
        var md = SessionExport.Markdown(new LogEntry("result", "before\n```\ncode\n```\nafter", null, false, "Bash"));
        Assert.StartsWith("#### Result · Bash\n\n````\nbefore", md, StringComparison.Ordinal);
        Assert.Contains("\n````\n", md);
        Assert.StartsWith("### You · 2026-11-11 05:00:00\n\nhi", SessionExport.Markdown(new LogEntry("user", "hi", new DateTime(2026, 11, 11, 5, 0, 0, DateTimeKind.Local), false)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_whole_session_has_a_title_and_facts_and_HTML_is_escaped()
    {
        var s = new Session([new LogEntry("user", "a <b> & c", null, false)], ["claude-x"], 10, 20, null, null);
        var md = SessionExport.Markdown(s, "My session");
        Assert.StartsWith("# My session\n\nclaude-x · 1 steps · 10 in · 20 out tokens", md, StringComparison.Ordinal);
        var html = SessionExport.Html(s, "My <session>");
        Assert.Contains("<title>My &lt;session&gt;</title>", html);
        Assert.Contains("a &lt;b&gt; &amp; c", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void Codex_puts_the_tokens_of_a_reply_on_its_first_step_and_ignores_a_repeated_count()
    {
        var count = """{"type":"event_msg","timestamp":"2026-11-11T05:00:05Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"cached_input_tokens":800,"output_tokens":50},"last_token_usage":{"input_tokens":1000,"cached_input_tokens":800,"output_tokens":50,"reasoning_output_tokens":10}}}}""";
        var s = new CodexSessionParser().Parse(Lines(
            """{"type":"turn_context","payload":{"model":"gpt-5"}}""",
            """{"type":"response_item","timestamp":"2026-11-11T05:00:00Z","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"hello"}]}}""",
            """{"type":"response_item","timestamp":"2026-11-11T05:00:02Z","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"hi"}]}}""",
            count,
            count));
        var usage = Assert.Single(s.Entries, e => e.Usage != null);
        Assert.Equal("assistant", usage.Role);
        Assert.Equal(("gpt-5", 200L, 50L, 800L, 10L), (usage.Usage!.Model, usage.Usage.Input, usage.Usage.Output, usage.Usage.CacheRead, usage.Usage.Reasoning));
    }

    [Fact]
    public void A_tool_result_the_app_marked_as_an_error_is_an_error_step()
    {
        var s = new ClaudeSessionParser().Parse(Lines(
            "{\"type\":\"assistant\",\"timestamp\":\"2026-11-11T05:00:00Z\",\"message\":{\"id\":\"m1\",\"model\":\"m\",\"content\":[{\"type\":\"tool_use\",\"id\":\"a\",\"name\":\"Bash\",\"input\":{\"command\":\"false\"}}]}}",
            "{\"type\":\"user\",\"timestamp\":\"2026-11-11T05:00:01Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"a\",\"content\":\"boom\",\"is_error\":true}]}}",
            "{\"type\":\"user\",\"timestamp\":\"2026-11-11T05:00:02Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"a\",\"content\":\"fine\"}]}}"));
        Assert.Equal([false, true, false], s.Entries.Select(e => e.IsError));
        Assert.Contains(" · failed", SessionExport.Markdown(s.Entries[1]), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_kind_of_step_has_its_own_heading_and_a_reply_shows_its_tokens()
    {
        var at = new DateTime(2026, 11, 11, 5, 0, 0, DateTimeKind.Local);
        var reply = new LogEntry("assistant", "done ", at, false) { Usage = new UsageEvent(at, "claude-opus-5-5", "p", 1200, 30, 800, 0, 0, false) };
        var md = SessionExport.Markdown(reply);
        Assert.StartsWith("### Assistant · 2026-11-11 05:00:00\n\ndone\n", md, StringComparison.Ordinal);
        Assert.Contains("*claude-opus-5-5 · 2,000 in (800 cached) · 30 out", md, StringComparison.Ordinal);
        Assert.StartsWith("#### Tool · Bash", SessionExport.Markdown(new LogEntry("tool", "ls", null, false, "Bash", Detail: "ls -la")), StringComparison.Ordinal);
        Assert.StartsWith("#### Tool · tool", SessionExport.Markdown(new LogEntry("tool", "x", null, false)), StringComparison.Ordinal);
        Assert.Equal("> **Thinking**\n>\n> a\n> b\n", SessionExport.Markdown(new LogEntry("thinking", "a\nb\n", null, false)));
        Assert.StartsWith("#### System", SessionExport.Markdown(new LogEntry("system", "note", null, false)), StringComparison.Ordinal);
        Assert.StartsWith("#### Note", SessionExport.Markdown(new LogEntry("", "note", null, false)), StringComparison.Ordinal);
    }
}
