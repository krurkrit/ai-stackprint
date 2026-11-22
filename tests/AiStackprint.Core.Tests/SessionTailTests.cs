// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>A followed session is parsed in pieces (what was written since the last read): the steps must be those of one parse of the whole file.</summary>
public sealed class SessionTailTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sp-tail-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly string[] _claude =
    [
        """{"type":"summary","isSidechain":true,"summary":"earlier work"}""",
        """{"type":"user","timestamp":"2026-11-11T05:00:00Z","message":{"role":"user","content":"fix the build"}}""",
        """{"type":"assistant","timestamp":"2026-11-11T05:00:05Z","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":100,"output_tokens":20},"content":[{"type":"thinking","thinking":"look"},{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"a.json"}}]}}""",
        """{"type":"assistant","timestamp":"2026-11-11T05:00:05Z","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":100,"output_tokens":20},"content":[{"type":"tool_use","id":"t2","name":"Bash","input":{"command":"ls"}}]}}""",
        "not json",
        """{"type":"user","timestamp":"2026-11-11T05:00:06Z","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"{}"},{"type":"tool_result","tool_use_id":"t2","content":"a.txt","is_error":true}]}}""",
        "",
        """{"type":"assistant","timestamp":"2026-11-11T05:00:07Z","agentId":"x9","message":{"id":"m2","usage":{"input_tokens":7,"output_tokens":3},"content":[{"type":"text","text":"done"}]}}""",
        """{"type":"user","timestamp":"2026-11-11T05:00:08Z","message":{"role":"user","content":"thanks"}}""",
    ];

    private static readonly string[] _codex =
    [
        """{"timestamp":"2026-11-11T05:00:00Z","type":"turn_context","payload":{"model":"gpt-5-codex"}}""",
        """{"timestamp":"2026-11-11T05:00:01Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"list files"}]}}""",
        """{"timestamp":"2026-11-11T05:00:02Z","type":"response_item","payload":{"type":"reasoning","summary":[{"text":"plan"}]}}""",
        """{"timestamp":"2026-11-11T05:00:03Z","type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"ls\"]}"}}""",
        """{"timestamp":"2026-11-11T05:00:04Z","type":"response_item","payload":{"type":"function_call_output","output":"a.txt"}}""",
        """{"timestamp":"2026-11-11T05:00:05Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":80,"output_tokens":9},"last_token_usage":{"input_tokens":80,"cached_input_tokens":20,"output_tokens":9},"model_context_window":200000}}}""",
        """{"timestamp":"2026-11-11T05:00:06Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"one file"}]}}""",
    ];

    private static void SameSession(Session expected, Session actual)
    {
        Assert.Equal(expected.Entries, actual.Entries);   // records: role, text, tool, language, agent, line, source, usage and error all the same
        Assert.Equal((expected.InputTokens, expected.OutputTokens, expected.PeakContext, expected.ContextWindow, expected.Start, expected.End, expected.AgentsBefore),
            (actual.InputTokens, actual.OutputTokens, actual.PeakContext, actual.ContextWindow, actual.Start, actual.End, actual.AgentsBefore));
        Assert.Equal(expected.Models.Order(), actual.Models.Order());
    }

    [Theory]
    [InlineData("claude-session")]
    [InlineData("codex-session")]
    public void A_session_fed_in_two_pieces_has_the_steps_of_one_parse_wherever_it_is_cut(string format)
    {
        var lines = format == "claude-session" ? _claude : _codex;
        var parser = LogParsers.Find(format)!;
        var whole = parser.Parse(LogReader.Utf8(lines));
        Assert.Contains(whole.Entries, e => e.Usage != null);   // the sample puts tokens on a step
        for (var cut = 0; cut <= lines.Length; cut++)
        {
            var feed = parser.Feed()!;
            feed.Add(LogReader.Utf8(lines[..cut]));
            feed.Add(LogReader.Utf8(lines[cut..]));
            SameSession(whole, feed.Snapshot());
        }
    }

    [Fact]
    public void A_tool_result_written_after_its_call_keeps_the_tools_name()
    {
        var feed = LogParsers.Find("claude-session")!.Feed()!;
        feed.Add(LogReader.Utf8(_claude[..4]));
        var before = feed.Snapshot().Entries.Count;
        Assert.Equal(before, feed.Add(LogReader.Utf8(_claude[4..6])));   // only new steps
        var results = feed.Snapshot().Entries.Skip(before).ToList();
        Assert.Equal(["Read", "Bash"], results.Select(e => e.Tool));
        Assert.Equal(".json", results[0].Lang);   // what Read returned is shown as the file's type
        Assert.Equal(6, results[0].Line);   // the line in the file, counted after the first piece's lines
    }

    [Fact]
    public void Codex_tokens_written_after_the_reply_change_its_first_step()
    {
        var feed = LogParsers.Find("codex-session")!.Feed()!;
        feed.Add(LogReader.Utf8(_codex[..5]));
        var first = feed.Add(LogReader.Utf8(_codex[5..6]));
        Assert.True(first < feed.Snapshot().Entries.Count);   // an earlier step changed: it says from where
        Assert.NotNull(feed.Snapshot().Entries[first].Usage);
    }

    [Fact]
    public void Complete_lines_leave_out_a_line_that_is_still_being_written()
    {
        var path = Path.Combine(_dir, "a.jsonl");
        File.WriteAllText(path, "{\"a\":1}\r\n{\"b\":2}\n{\"c\":");
        var piece = LogReader.CompleteLines(path, 0, false);
        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], piece.Rows.Select(r => Encoding.UTF8.GetString(r.Span)));
        Assert.False(piece.InLine);
        File.AppendAllText(path, "3}");   // the writer finished the line, without its line break yet
        piece = LogReader.CompleteLines(path, piece.End, false);
        Assert.Equal(["{\"c\":3}"], piece.Rows.Select(r => Encoding.UTF8.GetString(r.Span)));
        Assert.True(piece.InLine);   // a whole JSON value: kept, and the next read drops the rest of that line
        File.AppendAllText(path, "\n{\"d\":4}\n");
        piece = LogReader.CompleteLines(path, piece.End, piece.InLine);
        Assert.Equal(["{\"d\":4}"], piece.Rows.Select(r => Encoding.UTF8.GetString(r.Span)));
        Assert.Equal(new FileInfo(path).Length, piece.End);
    }

    [Fact]
    public void Complete_lines_skip_a_byte_order_mark_and_drop_a_cut_first_line()
    {
        var path = Path.Combine(_dir, "b.jsonl");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. "one\ntwo\n"u8]);
        Assert.Equal(["one", "two"], LogReader.CompleteLines(path, 0, false).Rows.Select(r => Encoding.UTF8.GetString(r.Span)));
        Assert.Equal(["two"], LogReader.CompleteLines(path, 5, true).Rows.Select(r => Encoding.UTF8.GetString(r.Span)));
    }

    private LogFile Session(string name, IEnumerable<string> lines)
    {
        var path = Path.Combine(_dir, name + ".jsonl");
        File.WriteAllLines(path, lines);
        return new LogFile("claude-code", name, path, new FileInfo(path).Length, DateTime.Now, null, "claude-session");
    }

    private static string Msg(string type, int second, string text, string? agent = null) =>
        "{\"type\":\"" + type + "\",\"timestamp\":\"2026-11-11T05:00:" + second.ToString("00") + "Z\"" + (agent == null ? "" : ",\"isSidechain\":true,\"agentId\":\"" + agent + "\"")
        + ",\"message\":{\"role\":\"" + type + "\",\"content\":\"" + text + "\"}}";

    [Fact]
    public void Lines_written_to_a_followed_session_are_read_alone_and_match_a_read_of_the_whole_file()
    {
        var f = Session("s", _claude[..4]);
        var tail = SessionTail.Open(f, 1 << 20)!;
        SameSession(SessionAgents.Read(f, 1 << 20), tail.Session);
        File.AppendAllLines(f.Path, _claude[4..]);
        var u = tail.Next();
        Assert.False(u.Rebuilt);
        Assert.Equal(["result", "result", "assistant", "user"], u.Added.Select(e => e.Role));
        Assert.Equal([6, 6, 8, 9], u.Added.Select(e => e.Line));   // their lines in the file
        SameSession(SessionAgents.Read(f, 1 << 20), u.Session);
        Assert.Empty(tail.Next().Added);   // nothing new
    }

    [Fact]
    public void A_half_written_line_is_read_once_it_is_whole()
    {
        var f = Session("s", [Msg("user", 1, "start")]);
        var tail = SessionTail.Open(f, 1 << 20)!;
        var next = Msg("assistant", 2, "reply");
        File.AppendAllText(f.Path, next[..20]);
        Assert.Empty(tail.Next().Added);
        File.AppendAllText(f.Path, next[20..] + "\n");
        Assert.Equal(["reply"], tail.Next().Added.Select(e => e.Text));
        SameSession(SessionAgents.Read(f, 1 << 20), tail.Session);
    }

    [Fact]
    public void A_sub_agents_new_steps_and_a_new_agent_file_are_added_with_their_place_in_its_file()
    {
        var agents = Path.Combine(_dir, "s", "subagents");
        Directory.CreateDirectory(agents);
        File.WriteAllLines(Path.Combine(agents, "agent-a1.jsonl"), [Msg("user", 2, "explore", "a1")]);
        var f = Session("s", [Msg("user", 1, "start")]);
        var tail = SessionTail.Open(f, 1 << 20)!;
        SameSession(SessionAgents.Read(f, 1 << 20), tail.Session);
        File.AppendAllLines(Path.Combine(agents, "agent-a1.jsonl"), [Msg("assistant", 3, "found", "a1")]);
        File.WriteAllLines(Path.Combine(agents, "agent-a2.jsonl"), [Msg("assistant", 4, "second agent", "a2")]);
        File.AppendAllLines(f.Path, [Msg("assistant", 5, "done")]);
        var u = tail.Next();
        Assert.Equal(["found", "second agent", "done"], u.Added.Select(e => e.Text));
        Assert.Equal([2L, 1, 2], u.Added.Select(e => e.Line));
        Assert.EndsWith("agent-a1.jsonl", u.Added[0].Source);
        Assert.Null(u.Added[2].Source);
        SameSession(SessionAgents.Read(f, 1 << 20), u.Session);
    }

    [Fact]
    public void An_agent_file_written_anew_reads_the_session_again_and_one_that_vanished_is_left_out()
    {
        var agents = Path.Combine(_dir, "s", "subagents");
        Directory.CreateDirectory(agents);
        var a1 = Path.Combine(agents, "agent-a1.jsonl");
        File.WriteAllLines(a1, [Msg("user", 2, "explore", "a1"), Msg("assistant", 3, "a long first answer", "a1")]);
        var f = Session("s", [Msg("user", 1, "start")]);
        var tail = SessionTail.Open(f, 1 << 20)!;
        Assert.Empty(tail.Next().Added);   // nothing new in the agent file either

        File.WriteAllLines(a1, [Msg("user", 4, "new", "a1")]);   // shorter: written anew
        var u = tail.Next();
        Assert.True(u.Rebuilt);
        Assert.Contains(u.Session.Entries, e => e.Text == "new");

        File.Delete(a1);
        File.AppendAllLines(f.Path, [Msg("assistant", 5, "done")]);
        Assert.Equal(["done"], tail.Next().Added.Select(e => e.Text));
    }

    [Fact]
    public void A_session_that_grew_far_past_what_was_kept_is_read_again_from_its_end()
    {
        var f = Session("s", [Msg("user", 1, "start")]);
        var tail = SessionTail.Open(f, 2000)!;
        File.AppendAllLines(f.Path, Enumerable.Range(2, 50).Select(n => Msg("assistant", n, "reply " + n)));
        var u = tail.Next();
        Assert.True(u.Rebuilt);
        Assert.True(tail.Cut);
        Assert.Equal("reply 51", u.Session.Entries[^1].Text);
    }

    [Fact]
    public void A_file_that_was_written_anew_is_read_again_from_the_start()
    {
        var f = Session("s", [Msg("user", 1, "start"), Msg("assistant", 2, "long reply")]);
        var tail = SessionTail.Open(f, 1 << 20)!;
        File.WriteAllLines(f.Path, [Msg("user", 3, "new")]);
        var u = tail.Next();
        Assert.True(u.Rebuilt);
        Assert.Equal(["new"], u.Session.Entries.Select(e => e.Text));
    }

    [Fact]
    public void A_session_cut_to_its_newest_bytes_reads_on_from_there()
    {
        var f = Session("s", Enumerable.Range(0, 40).Select(n => Msg(n % 2 == 0 ? "user" : "assistant", n, $"step {n}")));
        var tail = SessionTail.Open(f, 2000)!;
        Assert.True(tail.Cut);
        SameSession(LogWindow.Parse(f, LogWindow.End([f.Path], 2000)), tail.Session);
        Assert.Equal(22, tail.Session.Entries[0].Line);   // "step 21" is line 22 of the file: the lines are the file's, not counted from the cut
        Assert.Equal(tail.Span.From, LineIndex.ByteOfLine(f.Path, 22));
        File.AppendAllLines(f.Path, [Msg("user", 50, "more")]);
        Assert.Equal(["more"], tail.Next().Added.Select(e => e.Text));
    }

    [Fact]
    public void A_session_read_in_small_batches_has_the_steps_and_lines_of_one_parse()
    {
        var f = Session("s", [.. _claude, .. _claude]);
        var parser = LogParsers.Find("claude-session")!;
        var whole = parser.Parse(LogReader.Utf8Lines(f.Path, f.Size)).Entries;
        foreach (var batch in new long[] { 10, 200, 1000, 1 << 20 })   // smaller than a line: each line is read whole
        {
            Assert.Equal(whole, SessionSearch.Steps(parser.Feed()!, f.Path, TestContext.Current.CancellationToken, batch).ToList());
        }
    }

    [Fact]
    public void A_batch_ends_at_its_last_line_break()
    {
        var path = Path.Combine(_dir, "d.jsonl");
        File.WriteAllText(path, "{\"a\":1}\n{\"b\":2}\n{\"c\":3}");
        var piece = LogReader.CompleteLines(path, 0, false, 12);
        Assert.Equal(["{\"a\":1}"], piece.Rows.Select(r => Encoding.UTF8.GetString(r.Span)));
        Assert.Equal(8, piece.End);
        piece = LogReader.CompleteLines(path, piece.End, false, 3);   // shorter than the line: read until its line break
        Assert.Equal(["{\"b\":2}"], piece.Rows.Select(r => Encoding.UTF8.GetString(r.Span)));
        piece = LogReader.CompleteLines(path, piece.End, false, 100);
        Assert.Equal(["{\"c\":3}"], piece.Rows.Select(r => Encoding.UTF8.GetString(r.Span)));   // the end of the file: a whole JSON line without its break
    }

    [Fact]
    public void The_stretch_around_agents_keeps_a_dozen_steps_either_side_or_the_last_ones()
    {
        var steps = Enumerable.Range(0, 100).Select(n => new LogEntry("user", $"s{n}", new DateTime(2026, 11, 11, 5, 0, 0, DateTimeKind.Utc).AddSeconds(n), false)).ToList();
        var part = SessionAgents.Around(steps, steps[40].Time!.Value, steps[50].Time!.Value);
        Assert.Equal("s28", part[0].Text);
        Assert.Equal("s62", part[^1].Text);
        var late = SessionAgents.Around(steps, steps[99].Time!.Value.AddHours(1), steps[99].Time!.Value.AddHours(2));
        Assert.Equal(["s87", "s99"], [late[0].Text, late[^1].Text]);   // nothing starts that late: the last steps
    }

    [Fact]
    public void A_text_log_has_no_tail()
    {
        var path = Path.Combine(_dir, "app.log");
        File.WriteAllText(path, "INFO hi\n");
        Assert.Null(SessionTail.Open(new LogFile("app", "log", path, 8, DateTime.Now, null, "text-log"), 1 << 20));
    }

    [Fact]
    public void A_search_from_a_byte_counts_lines_from_the_line_given()
    {
        var path = Path.Combine(_dir, "c.log");
        File.WriteAllText(path, "alpha\nbeta\nalpha again\n");
        var hits = LogSearch.File(path, LogSearch.Pattern("alpha", false, false), 10, TestContext.Current.CancellationToken, 11, 3);
        Assert.Equal([3L], hits.Select(h => h.Line));
        Assert.Equal("alpha again", hits[0].Text);
    }
}
