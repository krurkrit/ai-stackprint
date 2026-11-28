// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class LimitPaceTests
{
    // Thursday 15:00 of a week that started Monday 00:00 and resets the next Monday 00:00: 87 of 168 hours gone
    private static readonly DateTime _now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Local);
    private static readonly DateTime _weekly = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Local), _five = _now.Date.AddHours(17);

    private static LimitReading At(int hour, double weekly, double? five = null, int day = 12) =>
        new(new DateTime(2026, 11, day, hour, 0, 0, DateTimeKind.Local),
            [.. new[] { five is { } f ? new LimitWindow("5-hour", f, _five) : null, new LimitWindow("Weekly", weekly, _weekly) }.OfType<LimitWindow>()]);

    private static LimitSnapshot Snap(LimitReading r) => new(null, r.Windows, r.At);

    [Theory]
    [InlineData("5-hour", 300.0)]
    [InlineData("Weekly", 10080.0)]
    [InlineData("Weekly · Sonnet", 10080.0)]
    [InlineData("Daily", 1440.0)]
    [InlineData("90-minute", 90.0)]
    [InlineData("Monthly", null)]
    [InlineData("Premium requests", null)]
    public void A_window_name_says_how_long_it_lasts(string name, double? minutes) =>
        Assert.Equal(minutes, LimitPacing.Length(name)?.TotalMinutes);

    [Fact]
    public void Running_hot_reaches_the_limit_before_the_reset_and_the_5_hour_window_before_it_resets()
    {
        List<LimitReading> history = [At(15, 40, day: 11), At(12, 60, 0), At(13, 62, 25), At(14, 64, 50)];
        var p = LimitPacing.Compute(Snap(At(15, 66, 75)), history, _now)!;

        Assert.Equal(("Weekly", _weekly.AddDays(-7), _weekly), (p.Name, p.Start, p.End));
        Assert.Equal(87.0 / 168 * 100, p.PaceNow, 6);
        Assert.Equal(26.0 / 24, p.PerHour, 6);   // over the last 24 hours of readings
        Assert.Equal(_now.AddHours(34 / (26.0 / 24)), p.FullAt);
        Assert.Equal(0, p.LeftAtReset);
        Assert.Equal(34 / 3.375, p.PerDayToLast, 6);   // 34 % over 3 days 9 hours
        Assert.Equal(new PacePoint(p.Start, 0, false), p.Points[0]);   // the reset, then the readings
        Assert.Equal([40.0, 60, 62, 64, 66], p.Points.Skip(1).Select(v => v.Used));

        var s = p.Short!;
        Assert.Equal(("5-hour", _now.Date.AddHours(12), _five, 75.0), (s.Name, s.Start, s.End, s.Used));
        Assert.Equal(_now.Date.AddHours(16), s.FullAt);   // 75 % in 3 hours: 100 % after 4
        Assert.Equal(8, s.Share!.Value, 6);   // weekly rose 6 while the 5-hour rose 75
        Assert.Equal((60.0, 68.0), s.Line(66));
    }

    [Fact]
    public void Leaving_usage_forecasts_what_is_left_and_with_little_history_uses_the_whole_week_so_far()
    {
        var p = LimitPacing.Compute(Snap(At(15, 20, 10)), [], _now)!;
        Assert.Equal(20.0 / 87, p.PerHour, 6);
        Assert.Null(p.FullAt);
        Assert.Equal(100 - 20 - 20.0 / 87 * 81, p.LeftAtReset, 6);
        Assert.Null(p.Short!.Share);   // not enough rise yet to tell
        Assert.Null(p.Short.Line(20));
        Assert.Null(p.Short.FullAt);   // 10 % in 3 hours: full after 30, past its reset
    }

    [Fact]
    public void A_full_window_is_at_the_limit_now()
    {
        var p = LimitPacing.Compute(Snap(At(15, 100, 100)), [], _now)!;
        Assert.Equal(_now, p.FullAt);
        Assert.Equal(_now, p.Short!.FullAt);
    }

    [Fact]
    public void Readings_of_an_earlier_week_or_another_window_do_not_join_the_line()
    {
        var lastWeek = new LimitReading(_now.AddDays(-5), [new("Weekly", 90, _weekly.AddDays(-7))]);
        var sonnet = new LimitReading(_now.AddHours(-1), [new("Weekly · Sonnet", 5, _weekly)]);
        var p = LimitPacing.Compute(Snap(At(15, 30)), [lastWeek, sonnet], _now)!;
        Assert.Equal([0.0, 30], p.Points.Select(v => v.Used));
        Assert.Null(p.Short);
    }

    [Fact]
    public void The_plain_weekly_window_is_chosen_and_a_window_without_a_reset_or_already_reset_gives_no_pace()
    {
        var both = new LimitSnapshot(null, [new("Weekly · Sonnet", 80, _weekly.AddDays(1)), new("Weekly", 10, _weekly)], _now);
        Assert.Equal("Weekly", LimitPacing.Compute(both, [], _now)!.Name);
        Assert.Null(LimitPacing.Compute(new(null, [new("Weekly", 10, null)], _now), [], _now));
        Assert.Null(LimitPacing.Compute(new(null, [new("Weekly", 10, _now.AddMinutes(-1))], _now), [], _now));
        Assert.Null(LimitPacing.Compute(new(null, [new("Premium requests", 10, _weekly)], _now), [], _now));
    }

    [Fact]
    public void A_5_hour_window_that_has_reset_counts_as_empty_until_the_next_one_starts()
    {
        var snap = new LimitSnapshot(null, [new("5-hour", 60, _now.AddHours(-1)), new("Weekly", 40, _weekly)], _now);
        var s = LimitPacing.Compute(snap, [], _now)!.Short!;
        Assert.Equal((0.0, _now, _now.AddHours(5)), (s.Used, s.Start, s.End));
        Assert.Null(s.FullAt);
    }
}
