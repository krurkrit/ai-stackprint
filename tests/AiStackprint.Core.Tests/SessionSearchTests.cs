// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Conversation and Compact search the steps of the whole session file and its sub-agents' files; every result is a place: file and line.</summary>
public sealed class SessionSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-ss-" + Guid.NewGuid().ToString("N"));

    public SessionSearchTests() => Directory.CreateDirectory(Path.Combine(_dir, "s1", "subagents"));

    public void Dispose() => Directory.Delete(_dir, true);

    private static string Msg(string role, int second, string text, string? agent = null) =>
        "{\"type\":\"" + role + "\",\"timestamp\":\"2026-11-11T05:00:" + second.ToString("00") + "Z\"" + (agent == null ? "" : ",\"isSidechain\":true,\"agentId\":\"" + agent + "\"")
        + ",\"message\":{\"role\":\"" + role + "\",\"content\":\"" + text + "\"}}";

    private LogFile Session(params string[] lines)
    {
        var path = Path.Combine(_dir, "s1.jsonl");
        File.WriteAllLines(path, lines);
        return new LogFile("claude-code", "s1", path, new FileInfo(path).Length, DateTime.Now, null, "claude-session");
    }

    [Fact]
    public void Results_are_steps_with_their_line_and_file_including_the_agents_files_and_the_count_is_complete()
    {
        File.WriteAllLines(Path.Combine(_dir, "s1", "subagents", "agent-a1.jsonl"), [Msg("assistant", 5, "agent has a needle", "a1")]);
        var f = Session(Msg("user", 1, "nothing"), Msg("assistant", 2, "a needle here"), Msg("user", 3, "another needle"));
        var hits = SessionSearch.File(f, LogQuery.Parse("needle", false, false, steps: true)!, 2, TestContext.Current.CancellationToken, out var total);
        Assert.Equal(3, total);   // all matches counted, two listed
        Assert.Equal([2L, 3L], hits.Select(h => h.Line));
        Assert.All(hits, h => Assert.Null(h.Source));

        var all = SessionSearch.File(f, LogQuery.Parse("needle", false, false, steps: true)!, 10, TestContext.Current.CancellationToken, out _);
        var agent = Assert.Single(all, h => h.Source != null);
        Assert.EndsWith("agent-a1.jsonl", agent.Source);
        Assert.Equal(1, agent.Line);   // its line in the agent's own file
    }

    [Fact]
    public void Role_filters_work_and_the_json_around_the_text_does_not_match()
    {
        var f = Session(Msg("user", 1, "needle from me"), Msg("assistant", 2, "needle from it"));
        Assert.Equal([1L], SessionSearch.File(f, LogQuery.Parse("role:user needle", false, false, steps: true)!, 10, TestContext.Current.CancellationToken, out _).Select(h => h.Line));
        Assert.Empty(SessionSearch.File(f, LogQuery.Parse("timestamp", false, false, steps: true)!, 10, TestContext.Current.CancellationToken, out _));   // a key of the JSON line, not text of a step
    }
}
