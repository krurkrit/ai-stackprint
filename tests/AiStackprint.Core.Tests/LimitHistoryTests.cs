// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class LimitHistoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("aistack-").FullName;
    private static readonly DateTime _now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Local);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Path_ => Path.Combine(_dir, "cache", "limit-history.json");

    private static LimitReading At(DateTime t, double used) => new(t, [new("Weekly", used, _now.AddDays(3))]);

    [Fact]
    public void Readings_are_kept_per_app_in_order_once_each_and_saved()
    {
        var h = new LimitHistory(Path_);
        Assert.Empty(h.Readings("claude-code"));
        h.Add("claude-code", At(_now, 30), _now);
        h.Add("claude-code", [At(_now.AddHours(-2), 20), At(_now, 30), new LimitReading(_now.AddHours(-1), [])], _now);   // a repeat and an empty one are skipped
        h.Add("codex-cli", At(_now, 5), _now);

        Assert.Equal([20.0, 30], h.Readings("claude-code").Select(r => r.Windows[0].UsedPercent));
        var again = new LimitHistory(Path_);
        Assert.Equal([20.0, 30], again.Readings("claude-code").Select(r => r.Windows[0].UsedPercent));
        Assert.Equal(_now.AddDays(3), again.Readings("codex-cli")[0].Windows[0].ResetsAt);
    }

    [Fact]
    public void Readings_older_than_eight_days_are_dropped()
    {
        var h = new LimitHistory(Path_);
        h.Add("claude-code", [At(_now.AddDays(-9), 90), At(_now.AddDays(-7), 10)], _now);
        Assert.Single(h.Readings("claude-code"));
        h.Add("claude-code", At(_now.AddDays(2), 40), _now.AddDays(2));   // later on, the one from 7 days before goes too
        Assert.Equal([40.0], h.Readings("claude-code").Select(r => r.Windows[0].UsedPercent));
    }

    [Fact]
    public void A_broken_file_starts_again()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
        File.WriteAllText(Path_, "{ not json");
        var h = new LimitHistory(Path_);
        Assert.Empty(h.Readings("claude-code"));
        h.Add("claude-code", At(_now, 1), _now);
        Assert.Single(new LimitHistory(Path_).Readings("claude-code"));
    }
}
