// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class WidgetRowsTests
{
    // Thursday 12 November 2026, 08:42
    private static readonly DateTime _now = new(2026, 11, 12, 8, 42, 0, DateTimeKind.Local);

    private static LimitSnapshot Snap(params LimitWindow[] w) => new("Max", [.. w], _now);

    private static LimitWindow Win(string name, double used, DateTime? reset) => new(name, used, reset);

    private static LimitStatus Ok(LimitSnapshot s) => new(LimitState.Ok, s, null, null, false);

    private static WidgetApp Build(LimitSnapshot s, IReadOnlyList<LimitReading>? history = null, LimitState state = LimitState.Ok, Func<string, bool>? shows = null) =>
        WidgetRows.Build("claude-code", "Claude Code", new LimitStatus(state, s, null, state == LimitState.Ok ? null : "Cannot reach the provider (offline?).", false),
            history ?? [], shows ?? (_ => true), _now)!;

    [Fact]
    public void A_short_window_filling_before_its_reset_warns_with_both_times()
    {
        // 75 % used 3 h 2 min into a 5-hour window that resets at 10:40: full at about 09:42
        var a = Build(Snap(Win("5-hour", 75, new DateTime(2026, 11, 12, 10, 40, 0, DateTimeKind.Local)), Win("Weekly", 10, _now.AddDays(5))));
        var r = a.Rows[0];
        Assert.True(r.Warns);
        Assert.StartsWith("Full about 09:", r.Line);
        Assert.EndsWith(" · resets 10:40", r.Line);
        Assert.Equal(60.7, r.Pace!.Value, 1);   // 3 h 2 min of 5 h (the window started at 05:40)
    }

    [Fact]
    public void A_short_window_that_lasts_only_says_when_it_resets()
    {
        var a = Build(Snap(Win("5-hour", 10, new DateTime(2026, 11, 12, 10, 40, 0, DateTimeKind.Local)), Win("Weekly", 10, _now.AddDays(5))));
        Assert.False(a.Rows[0].Warns);
        Assert.Equal("Resets 10:40", a.Rows[0].Line);
    }

    [Fact]
    public void The_long_window_says_when_it_runs_out_how_much_is_left_or_that_it_lasts()
    {
        var reset = new DateTime(2026, 11, 17, 22, 0, 0, DateTimeKind.Local);   // a Tuesday; the window started Tue 10 November 22:00
        var early = Build(Snap(Win("Weekly", 66, reset))).Rows[0];   // 66 % in 1 d 10.7 h: runs out on the weekend
        Assert.True(early.Warns);
        Assert.Matches(@"^Limit \w{3} \d\d:\d\d · resets Tue 22:00$", early.Line);

        var left = Build(Snap(Win("Weekly", 5, reset))).Rows[0];
        Assert.False(left.Warns);
        Assert.Matches(@"^\d+% left at the reset · Tue 22:00$", left.Line);

        var lasts = Build(Snap(Win("Weekly", 19, reset))).Rows[0];   // close to the even pace (20.6 %): little left
        Assert.Equal("Lasts to the reset · Tue 22:00", lasts.Line);
    }

    [Fact]
    public void Other_windows_say_when_they_reset_or_nothing()
    {
        var reset = new DateTime(2026, 11, 17, 22, 0, 0, DateTimeKind.Local);
        var a = Build(Snap(Win("Weekly", 5, reset), Win("Weekly · Sonnet", 30, reset), Win("Premium requests", 40, null)));
        Assert.Equal("Resets Tue 22:00", a.Rows[1].Line);
        Assert.Equal("", a.Rows[2].Line);
        Assert.Null(a.Rows[2].Pace);   // no known length: no pace mark
    }

    [Fact]
    public void Only_the_windows_shown_are_built_and_a_failed_reading_is_stale()
    {
        var s = Snap(Win("5-hour", 75, _now.AddHours(2)), Win("Weekly", 10, _now.AddDays(5)));
        var a = Build(s, shows: w => w == "Weekly");
        Assert.Equal(["Weekly"], a.Rows.Select(r => r.Window));
        Assert.Null(WidgetRows.Build("x", "X", Ok(s), [], _ => false, _now));
        Assert.Null(WidgetRows.Build("x", "X", new LimitStatus(LimitState.Off, null, null, null, false), [], _ => true, _now));

        var stale = Build(s, state: LimitState.Offline);
        Assert.True(stale.Stale);
        Assert.Equal("Cannot reach the provider (offline?).", stale.Status);
    }

    [Fact]
    public void Shows_treats_a_missing_app_as_all_of_its_windows()
    {
        var st = SettingsStore.Defaults();
        Assert.True(WidgetRows.Shows(st, "claude-code", "5-hour"));
        st.WidgetShow["claude-code"] = ["Weekly"];
        Assert.False(WidgetRows.Shows(st, "claude-code", "5-hour"));
        Assert.True(WidgetRows.Shows(st, "claude-code", "Weekly"));
    }

    [Fact]
    public void The_summary_tooltip_and_most_used_follow_the_readings()
    {
        var a = Build(Snap(Win("5-hour", 75, _now.AddHours(2)), Win("Weekly", 66, _now.AddDays(5))));
        Assert.Equal("Claude Code · 5-hour 75%, weekly 66%", WidgetRows.Summary(a));
        Assert.Equal(75, WidgetRows.MostUsed([a])!.Value.Used);
        Assert.Null(WidgetRows.MostUsed([]));
        Assert.Equal("AI Stackprint", WidgetRows.Tooltip([]));

        var many = Enumerable.Range(0, 5).Select(i => a with { Name = "Some app with a long name " + i }).ToList();
        var tip = WidgetRows.Tooltip(many);
        Assert.True(tip.Length <= WidgetRows.TooltipMax);
        Assert.Matches(@"\+\d more$", tip);
    }

    [Fact]
    public void The_pace_mark_is_how_far_the_window_has_run()
    {
        Assert.Equal(50, WidgetRows.PaceMark(Win("5-hour", 0, _now.AddHours(2.5)), _now)!.Value, 6);
        Assert.Equal(100, WidgetRows.PaceMark(Win("5-hour", 0, _now.AddHours(-1)), _now));
        Assert.Null(WidgetRows.PaceMark(Win("5-hour", 0, null), _now));
    }
}
