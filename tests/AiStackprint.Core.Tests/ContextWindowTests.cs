// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Uses of the context window: peak context per session, the model an app used last, Claude Code's skill-list budget.</summary>
public sealed class ContextWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ctxwin-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static IEnumerable<ReadOnlyMemory<byte>> Lines(params string[] lines) => lines.Select(l => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l));

    [Fact]
    public void Claude_peak_is_the_largest_main_request_and_sub_agents_do_not_count()
    {
        var s = new ClaudeSessionParser().Parse(Lines(
            """{"type":"assistant","timestamp":"2026-11-11T05:00:00Z","requestId":"r1","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":90000,"cache_creation_input_tokens":2000},"content":[{"type":"text","text":"a"}]}}""",
            """{"type":"assistant","timestamp":"2026-11-11T05:01:00Z","requestId":"r2","message":{"id":"m2","model":"claude-opus-5-5","usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":50000},"content":[{"type":"text","text":"b"}]}}""",
            """{"type":"assistant","timestamp":"2026-11-11T05:02:00Z","isSidechain":true,"requestId":"r3","message":{"id":"m3","model":"claude-opus-5-5","usage":{"input_tokens":500000,"output_tokens":5},"content":[{"type":"text","text":"c"}]}}"""));
        Assert.Equal(92_010, s.PeakContext);
        Assert.Equal(0, s.ContextWindow);   // from the model data
    }

    [Fact]
    public void Codex_peak_and_window_come_from_its_token_counts()
    {
        var s = new CodexSessionParser().Parse(Lines(
            """{"timestamp":"2026-11-11T05:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"output_tokens":10},"last_token_usage":{"input_tokens":1000,"output_tokens":10},"model_context_window":258400}}}""",
            """{"timestamp":"2026-11-11T05:01:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":121000,"output_tokens":20},"last_token_usage":{"input_tokens":120000,"output_tokens":10},"model_context_window":258400}}}""",
            """{"timestamp":"2026-11-11T05:02:00Z","type":"event_msg","payload":{"type":"token_count","info":null}}"""));
        Assert.Equal(120_000, s.PeakContext);
        Assert.Equal(258_400, s.ContextWindow);
    }

    [Fact]
    public void The_last_model_is_the_last_one_named_in_the_newest_log()
    {
        static string Line(string day, string id, string model) =>
            "{\"type\":\"assistant\",\"timestamp\":\"" + day + "T05:00:00Z\",\"requestId\":\"" + id + "\",\"message\":{\"id\":\"" + id + "\",\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}}";
        var older = Path.Combine(_dir, "a.jsonl");
        var newer = Path.Combine(_dir, "b.jsonl");
        File.WriteAllLines(older, [Line("2026-11-12", "x", "claude-haiku-5-5")]);
        File.WriteAllLines(newer, [Line("2026-11-10", "1", "claude-haiku-5-5"), Line("2026-11-11", "2", "claude-opus-5-5"), Line("2026-11-11", "3", "claude-sonnet-5-5"), Line("2026-11-11", "4", "claude-opus-5-5"),
            "{\"type\":\"user\",\"message\":{\"content\":\"set \\\"model\\\": \\\"gpt-6\\\" in config\"}}", Line("2026-11-11", "5", "<synthetic>")]);
        File.SetLastWriteTime(older, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Local));
        static LogFile F(string p) => new("claude", "Sessions", p, new FileInfo(p).Length, new FileInfo(p).LastWriteTime, null, "claude-session");
        Assert.Equal("claude-opus-5-5", UsageStats.LastModel([F(older), F(newer)], TestContext.Current.CancellationToken));
        Assert.Null(UsageStats.LastModel([], TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null, 8000)]
    [InlineData(200_000L, 8000)]
    [InlineData(1_000_000L, 40_000)]
    public void The_skill_list_budget_is_1_percent_of_the_window_in_characters(long? window, long budget) =>
        Assert.Equal(budget, SkillCheck.ClaudeListingBudget(window));

    [Fact]
    public void A_list_over_the_fallback_but_within_a_1M_window_is_not_reported()
    {
        // 120 skills × ~100 characters: over 8,000, under 1% of a 1M window (40,000)
        var items = Enumerable.Range(0, 120).Select(i => new Item("Skill", $"skill-{i:000}", new string('d', 80), "Claude Code", "User", Path.Combine(_dir, $"s{i}", "SKILL.md"))
        {
            App = "claude-code",
            Invokers = ["Agent"]
        }).ToList();
        static bool Listed(List<HealthIssue> h) => h.Any(i => i.Subject == "Claude Code skill list");
        Assert.True(Listed(Health.Compute(items)));
        Assert.False(Listed(Health.Compute(items, "claude-opus-5-5")));   // the built-in data: a 1M window
    }
}
