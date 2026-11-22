// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>The 5-hour block, the last model when the end of a log names none, and the health issues of every project.</summary>
public sealed class UsageWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sp-usage-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private static string Reply(DateTime t, string id, string model, int input) =>
        $$"""{"type":"assistant","timestamp":"{{t.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}}","message":{"id":"{{id}}","model":"{{model}}","usage":{"input_tokens":{{input}},"output_tokens":0""" + "}}}";

    private LogFile Log(string name, IEnumerable<string> lines)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, lines);
        var info = new FileInfo(path);
        return new LogFile("claude", "Sessions", path, info.Length, info.LastWriteTime, null, "claude-session");
    }

    [Fact]
    public void A_block_that_ended_more_than_five_hours_ago_is_not_shown()
    {
        var now = DateTime.Now;
        var live = UsageStats.Live([Log("old.jsonl", [Reply(now.AddHours(-7), "a", "claude-opus-5-5", 5)])], now, TestContext.Current.CancellationToken);
        Assert.Null(live.BlockStart);
        Assert.Null(live.Block);
        Assert.Equal(0, live.Last5h.Tokens);
        Assert.Equal(5, live.Last7d.Tokens);
    }

    [Fact]
    public void A_block_starts_on_the_hour_of_its_first_reply_and_a_gap_of_five_hours_starts_a_new_one()
    {
        var now = DateTime.Now;
        var live = UsageStats.Live([Log("s.jsonl", [Reply(now.AddHours(-30), "a", "m", 1), Reply(now.AddMinutes(-90), "b", "m", 2), Reply(now.AddMinutes(-10), "c", "m", 3)])], now, TestContext.Current.CancellationToken);
        var first = now.AddMinutes(-90);
        Assert.Equal(new DateTime(first.Year, first.Month, first.Day, first.Hour, 0, 0, first.Kind), live.BlockStart);
        Assert.Equal(5, live.Block!.Tokens);
    }

    [Fact]
    public void The_last_model_comes_from_the_last_day_of_the_log_when_its_end_names_none()
    {
        var day = DateTime.Now.AddDays(-1);
        var padding = Enumerable.Range(0, 3000).Select(i => """{"type":"user","message":{"content":"padding line """ + new string('x', 100) + " " + i + "\"}}");
        var f = Log("long.jsonl", [Reply(day.AddDays(-3), "a", "claude-haiku-5-5", 1), Reply(day, "b", "claude-sonnet-5-5", 1), Reply(day.AddMinutes(1), "c", "claude-sonnet-5-5", 1), Reply(day.AddMinutes(2), "d", "claude-opus-5-5", 1), .. padding]);
        Assert.True(f.Size > 256 * 1024);
        Assert.Equal("claude-sonnet-5-5", UsageStats.LastModel([f], TestContext.Current.CancellationToken));   // most replies on that day
        Assert.Null(UsageStats.LastModel([Log("none.jsonl", padding)], TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Usage_since_a_day_counts_only_the_sessions_with_replies_since()
    {
        var now = DateTime.Now;
        var r = UsageStats.Compute([Log("a.jsonl", [Reply(now.AddDays(-40), "a", "m", 7)]), Log("b.jsonl", [Reply(now.AddDays(-1), "b", "m", 3)])], now.AddDays(-7), TestContext.Current.CancellationToken);
        Assert.Equal(1, r.Sessions);
        Assert.Equal(3, r.Buckets.Sum(b => b.Input));
        var bucket = Assert.Single(UsageStats.Bucket([new UsageEvent(now, "m", "p", 1, 2, 0, 0, 0, false), new UsageEvent(now, "m", "p", 3, 4, 0, 0, 0, true)]));
        Assert.Equal((4L, 6L, 2, 1), (bucket.Input, bucket.Output, bucket.Messages, bucket.SubAgentMessages));
    }

    [Fact]
    public void The_health_page_has_each_project_findings_named_after_its_folder()
    {
        var project = Directory.CreateDirectory(Path.Combine(_dir, "myrepo")).FullName;
        File.WriteAllText(Path.Combine(project, "AGENTS.md"), "");
        var home = Directory.CreateDirectory(Path.Combine(_dir, "home")).FullName;
        var issues = ProjectChecks.ForHealth(AppCatalog.Load(), [project, Path.Combine(_dir, "gone")], home);
        var empty = Assert.Single(issues, i => i.Finding.Text == "AGENTS.md is empty.");
        Assert.Equal(("Projects", "myrepo", project), (empty.Area, empty.Subject, empty.Project));
    }
}
