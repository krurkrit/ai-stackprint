// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>The Logs summary: tokens and estimated cost per day, from the replies the session logs record.</summary>
public sealed class LogDailyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("logdaily-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private LogFile Session(string name, params string[] replies)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, replies);
        var info = new FileInfo(path);
        return new LogFile("claude-code", "Sessions", path, info.Length, info.LastWriteTime, null, "claude-session");
    }

    private static string Reply(string day, string id, int input, int output) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + day + "T10:00:00\",\"requestId\":\"r" + id + "\",\"message\":{\"id\":\"m" + id + "\",\"model\":\"claude-opus-5-5\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output + ",\"cache_read_input_tokens\":1000},\"content\":[{\"type\":\"text\",\"text\":\"x\"}]}}";

    [Fact]
    public void Tokens_and_cost_are_summed_per_day_newest_first_and_sessions_are_counted_per_day()
    {
        var a = Session("a.jsonl", Reply("2026-11-10", "1", 100, 50), Reply("2026-11-10", "2", 200, 70), Reply("2026-11-11", "3", 10, 5));
        var b = Session("b.jsonl", Reply("2026-11-11", "4", 20, 10));
        var days = LogSummary.Daily([a, b], null, TestContext.Current.CancellationToken);
        Assert.Equal([new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 10)], days.Select(d => d.Date));
        Assert.Equal((2, 30 + 2000, 15L), (days[0].Sessions, days[0].Input, days[0].Output));   // the request counts its cached part
        Assert.Equal((1, 300 + 2000, 120L), (days[1].Sessions, days[1].Input, days[1].Output));
        Assert.All(days, d => Assert.True(d.Cost >= 0));
    }

    [Fact]
    public void A_period_leaves_out_the_days_before_it_and_the_stats_carry_the_totals()
    {
        var a = Session("a.jsonl", Reply("2026-11-10", "1", 100, 50), Reply("2026-11-11", "2", 10, 5));
        var stats = LogSummary.Compute([a], new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Local).AddYears(-1));
        Assert.Equal(2, stats.Daily.Count);
        Assert.Equal(stats.Daily.Sum(d => d.Cost), stats.Cost);
        var since = LogSummary.Daily([a], new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Local), TestContext.Current.CancellationToken);
        Assert.Equal([new DateOnly(2026, 11, 11)], since.Select(d => d.Date));
    }
}
