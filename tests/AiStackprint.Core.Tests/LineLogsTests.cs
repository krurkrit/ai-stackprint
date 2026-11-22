// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Text logs: each format found in the apps' own logs becomes steps with a time, a level and a message.</summary>
public sealed class LineLogsTests
{
    private static Session Parse(params string[] lines) => LineLogs.Parse(lines.Select(l => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l)));

    [Fact]
    public void Bracketed_lines_of_editors_and_desktop_apps_are_read()
    {
        var s = Parse(
            "2026-07-10 15:39:29.124 [info] update#setState idle",
            "[2026-08-07 15:23:49.447] [warn]  Copied shim",
            "2026-11-19T15:29:00.045Z [ERROR] Model request failed");
        Assert.Equal(["info", "warn", "error"], s.Entries.Select(e => e.Role));
        Assert.Equal(["update#setState idle", "Copied shim", "Model request failed"], s.Entries.Select(e => e.Text));
        Assert.Equal(new DateTime(2026, 7, 10, 15, 39, 29, 124, DateTimeKind.Local), s.Entries[0].Time);
        Assert.NotNull(s.Start);
        Assert.NotNull(s.End);
    }

    [Fact]
    public void Lines_that_start_no_entry_join_the_line_above()
    {
        var s = Parse(
            "2026-08-26 11:22:27 [info] Starting app {",
            "  appVersion: '0.13.19',",
            "}",
            "2026-08-26 11:22:28 [info] Done");
        Assert.Equal(2, s.Entries.Count);
        Assert.Equal("Starting app {\n  appVersion: '0.13.19',\n}", s.Entries[0].Text);
    }

    [Fact]
    public void Key_value_lines_give_their_message_and_keep_the_rest_as_detail()
    {
        var s = Parse(
            "timestamp=2026-06-17T10:56:15.601Z level=INFO run=a0aee087 message=\"creating \\\"instance\\\"\" directory=\"C:\\\\work\"",
            "timestamp=2026-06-17T10:56:15.602Z level=DEBUG run=a0aee087 message=fromDirectory");
        Assert.Equal(["info", "debug"], s.Entries.Select(e => e.Role));
        Assert.Equal("creating \"instance\"", s.Entries[0].Text);
        Assert.Equal("fromDirectory", s.Entries[1].Text);
        Assert.Contains("run=a0aee087", s.Entries[0].Detail);
        Assert.DoesNotContain("message=", s.Entries[0].Detail);
    }

    [Fact]
    public void JSON_lines_give_the_level_and_the_message_field()
    {
        var s = Parse(
            """{"timestamp":"2026-08-07T08:25:58.775268Z","level":"INFO","fields":{"message":"CLI command executed"},"target":"x"}""",
            """{"timestamp":"2026-08-07T08:34:35.700712Z","level":"WARN","message":"Failed to load"}""");
        Assert.Equal(["info", "warn"], s.Entries.Select(e => e.Role));
        Assert.Equal(["CLI command executed", "Failed to load"], s.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Glog_lines_give_a_level_letter_and_a_date_without_a_year()
    {
        var s = Parse(
            "ERROR: logging before google.Init: I1119 22:11:27.954770       1 server.go:2552] Starting language server",
            "W1120 18:19:30.089376  226802 http_helpers.go:709] Slow request");
        Assert.Equal(["info", "warn"], s.Entries.Select(e => e.Role));
        Assert.Equal("Starting language server", s.Entries[0].Text);
        Assert.Equal(11, s.Entries[0].Time!.Value.Month);
        Assert.True(s.Entries[0].Time!.Value <= DateTime.Now.AddDays(1));
    }

    [Fact]
    public void A_file_in_no_known_format_gives_no_steps()
    {
        Assert.Empty(Parse("hello", "world").Entries);
        Assert.Empty(Parse().Entries);
    }

    [Fact]
    public void Only_the_roles_of_text_log_levels_count_as_levels()
    {
        Assert.True(LineLogs.IsLevel("error"));
        Assert.True(LineLogs.IsLevel("log"));
        Assert.False(LineLogs.IsLevel("user"));
        Assert.False(LineLogs.IsLevel("tool"));
        Assert.True(LineLogs.Is("text-log"));
        Assert.False(LineLogs.Is("claude-session"));
        Assert.False(LineLogs.Is(null));
    }

    [Fact]
    public void Level_names_of_other_loggers_map_to_the_common_ones()
    {
        var s = Parse(
            "2026-01-01 10:00:00 [FATAL] a",
            "2026-01-01 10:00:01 [WARNING] b",
            "2026-01-01 10:00:02 [VERBOSE] c",
            "2026-01-01 10:00:03 [whatever] d");
        Assert.Equal(["error", "warn", "trace", "log"], s.Entries.Select(e => e.Role));
    }

    [Fact]
    public void Orca_trace_spans_give_a_time_a_level_from_their_exit_and_a_summary()
    {
        var s = Parse(
            """{"type":"effect-span","name":"git.exec","traceId":"t","spanId":"s","kind":"internal","startTimeUnixNano":"1789486915457000000","endTimeUnixNano":"1789486915636000000","durationMs":179,"attributes":{"kind":"git","git.subcommand":"worktree"},"events":[],"exit":{"_tag":"Success"}}""",
            """{"type":"effect-span","name":"crash.breadcrumb","startTimeUnixNano":"1789486916401000000","durationMs":0,"attributes":{"breadcrumb.name":"main_process_lifecycle_started"},"exit":{"_tag":"Success"}}""",
            """{"type":"effect-span","name":"git.exec","startTimeUnixNano":"1789486917000000000","durationMs":166,"attributes":{"git.subcommand":"remote"},"exit":{"_tag":"Failure","cause":"Error: Command failed: git remote get-url upstream"}}""");
        Assert.Equal(["info", "debug", "error"], s.Entries.Select(e => e.Role));
        Assert.Equal("git.exec · git worktree · 179 ms", s.Entries[0].Text);
        Assert.Equal("crash.breadcrumb · main_process_lifecycle_started", s.Entries[1].Text);
        Assert.StartsWith("git.exec · git remote · 166 ms\nError: Command failed", s.Entries[2].Text, StringComparison.Ordinal);
        Assert.Equal(2026, s.Entries[0].Time!.Value.Year);
    }

    [Fact]
    public void Orca_daemon_events_give_the_event_and_a_few_of_its_fields()
    {
        var s = Parse(
            """{"src":"daemon","ts":"2026-09-15T15:42:14.106Z","pid":4580,"event":"startup","protocolVersion":36}""",
            """{"src":"daemon","ts":"2026-09-15T15:42:15.000Z","pid":4580,"event":"client-error","code":7}""");
        Assert.Equal(["info", "error"], s.Entries.Select(e => e.Role));
        Assert.Equal("startup pid=4580 protocolVersion=36", s.Entries[0].Text);
    }

    [Fact]
    public void Json_lines_cut_off_or_without_their_fields_are_not_read_as_entries_of_their_kind()
    {
        var s = Parse(
            """{"timestamp":"2026-01-01T00:00:00Z","level":"WARN","message":"top-level message"}""",
            """{"timestamp":"2026-01-01T00:00:01Z","level":"INFO","fields":{""",
            """{"type":"effect-span","name":"cut""",
            """{"type":"effect-span","name":"no start"}""",
            """{"event":"cut""",
            """{"event":"no time"}""");
        var e = Assert.Single(s.Entries);   // the other lines are not entries: they go on the one before
        Assert.Equal("warn", e.Role);
        Assert.StartsWith("top-level message\n", e.Text, StringComparison.Ordinal);
        Assert.EndsWith("""{"event":"no time"}""", e.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_glog_date_that_would_be_in_the_future_is_last_year()
    {
        var now = DateTime.Now;
        var day = now.AddDays(3);
        var s = Parse($"W{day:MMdd} 08:00:00.000000 1 server.go:10] later");
        var expected = new DateTime(now.Year, day.Month, day.Day, 8, 0, 0, DateTimeKind.Local) > now.AddDays(1) ? now.Year - 1 : now.Year;
        Assert.Equal(("warn", expected), (s.Entries[0].Role, s.Entries[0].Time!.Value.Year));
    }
}
