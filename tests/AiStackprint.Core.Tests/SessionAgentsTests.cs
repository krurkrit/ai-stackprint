// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Claude Code's sub-agents live in <c>&lt;session&gt;/subagents/agent-&lt;id&gt;.jsonl</c>: they are read with the session and put among its steps by time.</summary>
public sealed class SessionAgentsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-agents-" + Guid.NewGuid().ToString("N"));

    public SessionAgentsTests() => Directory.CreateDirectory(Path.Combine(_dir, "s1", "subagents"));

    public void Dispose() => Directory.Delete(_dir, true);

    private static string Msg(string type, int second, string text, string? agent = null) =>
        "{\"type\":\"" + type + "\",\"timestamp\":\"2026-11-11T05:00:" + second.ToString("00") + "Z\"" + (agent == null ? "" : ",\"isSidechain\":true,\"agentId\":\"" + agent + "\"")
        + ",\"message\":{\"role\":\"" + type + "\",\"content\":\"" + text + "\"}}";

    private LogFile Main(params string[] lines)
    {
        var path = Path.Combine(_dir, "s1.jsonl");
        File.WriteAllLines(path, lines);
        return new LogFile("claude-code", "s1", path, new FileInfo(path).Length, DateTime.Now, null, "claude-session");
    }

    [Fact]
    public void An_agents_steps_are_put_among_the_sessions_by_time_and_make_a_branch_in_the_graph()
    {
        File.WriteAllLines(Path.Combine(_dir, "s1", "subagents", "agent-a1.jsonl"), [Msg("user", 2, "explore it", "a1"), Msg("assistant", 3, "found it", "a1")]);
        var session = SessionAgents.Read(Main(Msg("user", 1, "start"), Msg("assistant", 4, "got the answer")), 1 << 20);
        Assert.Equal(["start", "explore it", "found it", "got the answer"], session.Entries.Select(e => e.Text));
        Assert.Equal([false, true, true, false], session.Entries.Select(e => e.SubAgent));
        Assert.All(session.Entries.Where(e => e.SubAgent), e => Assert.Equal(0, e.Line));   // no place in the main file: no bookmark
        Assert.Equal(1, session.Entries[0].Line);
        var graph = SessionGraph.Layout(session.Entries)!;
        Assert.Equal(2, graph.Lanes);   // the main line and one branch
    }

    [Fact]
    public void Two_agents_that_run_at_the_same_time_each_keep_their_steps_together()
    {
        File.WriteAllLines(Path.Combine(_dir, "s1", "subagents", "agent-a1.jsonl"), [Msg("assistant", 2, "one a", "a1"), Msg("assistant", 5, "one b", "a1")]);
        File.WriteAllLines(Path.Combine(_dir, "s1", "subagents", "agent-a2.jsonl"), [Msg("assistant", 3, "two a", "a2"), Msg("assistant", 4, "two b", "a2")]);
        var session = SessionAgents.Read(Main(Msg("user", 1, "start"), Msg("assistant", 6, "done")), 1 << 20);
        Assert.Equal(["start", "one a", "two a", "two b", "one b", "done"], session.Entries.Select(e => e.Text));
        Assert.Equal(3, SessionGraph.Layout(session.Entries)!.Lanes);   // two branches side by side
    }

    [Fact]
    public void A_session_without_agent_files_is_just_the_session()
    {
        Directory.Delete(Path.Combine(_dir, "s1"), true);
        var session = SessionAgents.Read(Main(Msg("user", 1, "start")), 1 << 20);
        Assert.Single(session.Entries);
    }

    [Fact]
    public void The_part_around_an_earlier_agent_has_its_branch_and_the_files_own_line_numbers()
    {
        File.WriteAllLines(Path.Combine(_dir, "s1", "subagents", "agent-a1.jsonl"), [Msg("assistant", 11, "older", "a1")]);
        File.WriteAllLines(Path.Combine(_dir, "s1", "subagents", "agent-a2.jsonl"), [Msg("assistant", 31, "newer", "a2"), Msg("assistant", 32, "newer b", "a2")]);
        var lines = Enumerable.Range(0, 60).Select(i => Msg(i % 2 == 0 ? "user" : "assistant", i, "m" + i)).ToArray();
        var f = Main(lines);
        var window = SessionAgents.ReadAround(f, new DateTime(2026, 11, 11, 5, 0, 50, DateTimeKind.Utc).ToLocalTime(), 400)!;
        Assert.Contains(window.Entries, e => e.SubAgent && e.Text == "newer");
        Assert.DoesNotContain(window.Entries, e => e.Text == "older");   // the older agent is the next step back
        Assert.Equal(1, window.AgentsBefore);
        Assert.Equal(32, window.Entries.First(e => !e.SubAgent && e.Text == "m31").Line);   // the file's own line number
        Assert.True(window.Entries.Count < 60);
        Assert.Equal(2, SessionGraph.Layout(window.Entries)!.Lanes);
        Assert.Null(SessionAgents.ReadAround(f, new DateTime(2026, 11, 11, 5, 0, 5, DateTimeKind.Utc).ToLocalTime(), 400));   // no agent started before this
    }

    [Fact]
    public void An_agent_from_before_the_part_that_was_read_is_counted_not_shown()
    {
        File.WriteAllLines(Path.Combine(_dir, "s1", "subagents", "agent-a1.jsonl"), [Msg("assistant", 1, "early", "a1")]);
        var main = LogParsers.Find("claude-session")!.Parse(LogReader.Utf8([Msg("user", 30, "late")]));   // the part that was read starts at second 30
        var session = SessionAgents.Merge(main, Path.Combine(_dir, "s1.jsonl"), true);
        Assert.Single(session.Entries);
        Assert.Equal(1, session.AgentsBefore);
    }
}
