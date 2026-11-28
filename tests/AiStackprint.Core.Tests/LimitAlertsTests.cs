// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class LimitAlertsTests
{
    private static readonly DateTime _now = new(2026, 11, 12, 8, 42, 0, DateTimeKind.Local);
    private static readonly DateTime _short = new(2026, 11, 12, 10, 40, 0, DateTimeKind.Local), _weekly = new(2026, 11, 17, 22, 0, 0, DateTimeKind.Local);
    private readonly LimitAlerts _alerts = new();
    private readonly Dictionary<string, DateTime> _sent = [];
    private readonly Settings _s = SettingsStore.Defaults();

    private static LimitSnapshot Snap(DateTime at, double five, double week, DateTime? weekReset = null) =>
        new("Max", [new("5-hour", five, _short), new("Weekly", week, weekReset ?? _weekly)], at);

    private List<Alert> Check(LimitSnapshot s, DateTime? now = null, DateTime? paused = null) =>
        _alerts.Check("claude-code", "Claude Code", s, [], _s, _sent, paused, now ?? s.FetchedAt, (now ?? s.FetchedAt).ToUniversalTime(), out _);

    [Fact]
    public void The_short_window_alerts_once_per_threshold_crossed_upward()
    {
        _s.AlertEarlyHours = 0;
        Assert.Empty(Check(Snap(_now.AddMinutes(-30), 60, 10)));
        var a = Assert.Single(Check(Snap(_now, 82, 11)));
        Assert.Equal(AlertKind.ShortNearlyFull, a.Kind);
        Assert.Equal("Claude Code: 5-hour limit at 82%", a.Title);
        Assert.EndsWith("It resets at 10:40.", a.Text);
        Assert.True(a.Open && a.Pause);
        Assert.Empty(Check(Snap(_now.AddMinutes(5), 84, 11)));   // still above 80: not again
        Assert.Empty(Check(Snap(_now.AddMinutes(10), 70, 11)));   // falling never alerts
        Assert.Empty(Check(Snap(_now.AddMinutes(15), 85, 11)));   // 80 again in the same window: sent already
        Assert.Contains("limit at 96%", Assert.Single(Check(Snap(_now.AddMinutes(20), 96, 12))).Title);
    }

    [Fact]
    public void A_jump_past_both_thresholds_sends_one_alert()
    {
        _s.AlertEarlyHours = 0;
        Check(Snap(_now.AddMinutes(-30), 10, 10));
        Assert.Single(Check(Snap(_now, 97, 12)));
    }

    [Fact]
    public void The_long_window_running_out_early_alerts_once_a_day()
    {
        var a = Assert.Single(Check(Snap(_now, 10, 66)), x => x.Kind == AlertKind.RunsOutEarly);
        Assert.Equal("Claude Code: weekly limit runs out early", a.Title);
        Assert.Matches(@"^At this pace it's reached \w{3} \d\d:\d\d, \d+ d \d+ h before the reset\. Slow to \d+\.\d% a day to last\.$", a.Text);
        Assert.DoesNotContain(Check(Snap(_now.AddHours(2), 10, 70)), x => x.Kind == AlertKind.RunsOutEarly);
        Assert.Contains(Check(Snap(_now.AddDays(1), 10, 80), _now.AddDays(1)), x => x.Kind == AlertKind.RunsOutEarly);
    }

    [Fact]
    public void A_new_long_window_sends_the_reset_alert_with_the_last_reading()
    {
        _s.AlertEarlyHours = 0;
        Check(Snap(_now, 10, 94));
        var a = Assert.Single(Check(Snap(_now.AddMinutes(5), 10, 0, _weekly.AddDays(7))), x => x.Kind == AlertKind.Reset);
        Assert.Equal("Claude Code: weekly limit reset", a.Title);
        Assert.Equal("You used 94% last week. Next reset Tue 22:00.", a.Text);
        Assert.False(a.Open);

        _s.AlertReset = false;
        var other = new LimitAlerts();
        other.Check("x", "X", Snap(_now, 10, 50), [], _s, _sent, null, _now, _now.ToUniversalTime(), out _);
        Assert.Empty(other.Check("x", "X", Snap(_now.AddMinutes(5), 10, 0, _weekly.AddDays(7)), [], _s, _sent, null, _now, _now.ToUniversalTime(), out _));
    }

    [Fact]
    public void Usage_left_alerts_a_day_before_the_reset()
    {
        var late = _weekly.AddHours(-20);   // 20 h before the reset, 61 % used over 6 days: about 39 % left
        var a = Assert.Single(Check(Snap(late, 0, 61), late), x => x.Kind == AlertKind.UsageLeft);
        Assert.Matches(@"^Claude Code: \d+% left before the reset$", a.Title);
        Assert.Matches(@"^It resets Tue 22:00\. You can use about \d+\.\d% a day\.$", a.Text);
        _s.AlertLeftHours = 0;
        Assert.Empty(new LimitAlerts().Check("x", "X", Snap(late, 0, 61), [], _s, new Dictionary<string, DateTime>(), null, late, late.ToUniversalTime(), out _));
    }

    [Fact]
    public void While_paused_alerts_are_skipped_not_queued()
    {
        _s.AlertEarlyHours = 0;
        Check(Snap(_now.AddMinutes(-30), 10, 10));
        Assert.Empty(Check(Snap(_now, 85, 11), paused: _now.ToUniversalTime().AddHours(1)));
        Assert.Empty(Check(Snap(_now.AddHours(2), 86, 11)));   // after the pause: the 80 % one is not sent late
    }

    [Fact]
    public void Switched_off_nothing_is_sent_and_old_keys_are_dropped()
    {
        _s.AlertsEnabled = false;
        _sent["short|old"] = _now.ToUniversalTime().AddDays(-9);
        _sent["short|new"] = _now.ToUniversalTime().AddDays(-1);
        Assert.Empty(_alerts.Check("claude-code", "Claude Code", Snap(_now, 99, 99), [], _s, _sent, null, _now, _now.ToUniversalTime(), out var changed));
        Assert.True(changed);
        Assert.Equal(["short|new"], _sent.Keys);
    }

    [Fact]
    public void Spans_read_in_days_hours_or_minutes()
    {
        Assert.Equal("2 d 15 h", LimitAlerts.Span(new TimeSpan(2, 15, 10, 0)));
        Assert.Equal("15 h", LimitAlerts.Span(TimeSpan.FromHours(15.5)));
        Assert.Equal("40 min", LimitAlerts.Span(TimeSpan.FromMinutes(40)));
    }
}
