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

    private string Folder => Path.Combine(_dir, "cache", "limits");

    private LimitHistory New(DateTime? now = null) => new(Folder, () => now ?? _now);

    private static LimitReading At(DateTime t, double used) => new(t, [new("Weekly", used, _now.AddDays(3))]);

    [Fact]
    public void Readings_are_kept_per_app_in_order_once_each_and_appended_to_the_month_file()
    {
        var h = New();
        Assert.Empty(h.Readings("claude-code"));
        h.Add("claude-code", At(_now, 30), _now);
        h.Add("claude-code", [At(_now.AddHours(-2), 20), At(_now, 30), new LimitReading(_now.AddHours(-1), [])], _now);   // a repeat and an empty one are skipped
        h.Add("codex-cli", At(_now, 5), _now);

        Assert.Equal([20.0, 30], h.Readings("claude-code").Select(r => r.Windows[0].UsedPercent));
        var file = Path.Combine(Folder, "2026-11.jsonl");
        Assert.Equal(3, File.ReadAllLines(file).Length);   // one line per reading, never rewritten
        Assert.Contains("\"app\":\"codex-cli\"", File.ReadAllText(file));
        var again = New();
        Assert.Equal([20.0, 30], again.Readings("claude-code").Select(r => r.Windows[0].UsedPercent));
        Assert.Equal(_now.AddDays(3), again.Readings("codex-cli")[0].Windows[0].ResetsAt);
    }

    [Fact]
    public void Readings_older_than_eight_days_are_dropped()
    {
        var h = New();
        h.Add("claude-code", [At(_now.AddDays(-9), 90), At(_now.AddDays(-7), 10)], _now);
        Assert.Single(h.Readings("claude-code"));
        h.Add("claude-code", At(_now.AddDays(2), 40), _now.AddDays(2));   // later on, the one from 7 days before goes too
        Assert.Equal([40.0], h.Readings("claude-code").Select(r => r.Windows[0].UsedPercent));
        Assert.Equal([40.0], New(_now.AddDays(2)).Readings("claude-code").Select(r => r.Windows[0].UsedPercent));   // and when read again
    }

    [Fact]
    public void A_reading_goes_to_the_file_of_its_month_and_both_months_are_read()
    {
        var first = new DateTime(2026, 11, 30, 23, 0, 0, DateTimeKind.Local);
        var h = New(first.AddHours(2));
        h.Add("claude-code", [At(first, 10), At(first.AddHours(2), 12)], first.AddHours(2));
        Assert.True(File.Exists(Path.Combine(Folder, "2026-11.jsonl")));
        Assert.True(File.Exists(Path.Combine(Folder, "2026-12.jsonl")));
        Assert.Equal(2, New(first.AddHours(2)).Readings("claude-code").Count);
    }

    [Fact]
    public void Files_older_than_ninety_days_are_deleted()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "2026-07.jsonl"), "");
        File.WriteAllText(Path.Combine(Folder, "2026-09.jsonl"), "");
        _ = New().Readings("claude-code");
        Assert.False(File.Exists(Path.Combine(Folder, "2026-07.jsonl")));   // ended more than 90 days before 12 November
        Assert.True(File.Exists(Path.Combine(Folder, "2026-09.jsonl")));
    }

    [Fact]
    public void The_file_of_earlier_versions_is_moved_in_once()
    {
        var old = Path.Combine(_dir, "cache", "limit-history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, """{"claude-code":[{"at":"2026-11-12T13:00:00","windows":[{"name":"Weekly","usedPercent":25,"resetsAt":"2026-11-15T15:00:00"}]}]}""");
        Assert.Equal([25.0], New().Readings("claude-code").Select(r => r.Windows[0].UsedPercent));
        Assert.False(File.Exists(old));
        Assert.Single(New().Readings("claude-code"));   // read from the month file now, not twice
    }

    [Fact]
    public void A_broken_line_or_old_file_is_skipped()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(_dir, "cache", "limit-history.json"), "{ not json");
        File.WriteAllText(Path.Combine(Folder, "2026-11.jsonl"), "{ cut sh\n");
        var h = New();
        Assert.Empty(h.Readings("claude-code"));
        h.Add("claude-code", At(_now, 1), _now);
        Assert.Single(New().Readings("claude-code"));
    }
}
