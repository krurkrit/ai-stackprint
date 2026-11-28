// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>One limit window in the usage widget. <paramref name="Pace"/>: where an even pace would be now (0–100), null when the window's length is not known.</summary>
public record WidgetRow(string Window, double Used, double? Pace, string Line, bool Warns);

/// <summary>
/// One app's block in the widget. <paramref name="Stale"/>: the last reading is kept while a new one fails; the title then says when it was taken
/// (<paramref name="At"/>) and <paramref name="Status"/> says why.
/// </summary>
public record WidgetApp(string Id, string Name, string? Plan, List<WidgetRow> Rows, DateTime At, bool Stale, string? Status);

/// <summary>The rows and texts of the usage widget, the tray tooltip and the tray menu's usage line, from the readings and <see cref="LimitPacing"/>.</summary>
public static class WidgetRows
{
    private static readonly CultureInfo _en = CultureInfo.InvariantCulture;

    /// <summary>A window with more left than this at the reset says how much.</summary>
    public const double LeftWorthSaying = 10;

    /// <summary>Tooltips are cut at this many characters (Windows).</summary>
    public const int TooltipMax = 127;

    /// <summary>Whether the widget shows a window: an app missing from <see cref="Settings.WidgetShow"/> shows all of its windows.</summary>
    public static bool Shows(Settings s, string appId, string window) => !s.WidgetShow.TryGetValue(appId, out var list) || list.Contains(window);

    /// <summary>The app's block, with only the windows <paramref name="shows"/> (see <see cref="Shows"/>); null when it has no reading yet or shows none of them.</summary>
    public static WidgetApp? Build(string id, string name, LimitStatus st, IReadOnlyList<LimitReading> history, Func<string, bool> shows, DateTime now)
    {
        if (st.Data is not { Windows.Count: > 0 } data)
        {
            return null;
        }

        var pace = LimitPacing.Compute(data, history, now);
        var rows = data.Windows.Where(w => shows(w.Name)).Select(w => Row(w, pace, now)).ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        var stale = st.State is not (LimitState.Ok or LimitState.Off);
        return new(id, name, data.Plan, rows, data.FetchedAt, stale, stale ? st.Message : null);
    }

    /// <summary>A window's row: its even-pace mark and the line under it (see the table in docs/decisions.md › Tray and widget).</summary>
    public static WidgetRow Row(LimitWindow w, LimitPace? pace, DateTime now)
    {
        string line;
        var warns = false;
        if (pace?.Short is { } sh && sh.Name == w.Name)
        {
            warns = sh.FullAt is { } f && f < sh.End;
            line = warns ? $"Full about {Time(sh.FullAt!.Value)} · resets {Time(sh.End)}" : $"Resets {Time(sh.End)}";
        }
        else if (pace != null && pace.Name == w.Name)
        {
            warns = pace.FullAt is { } f && f < pace.End;
            line = warns ? $"Limit {Day(pace.FullAt!.Value)} · resets {Day(pace.End)}"
                : pace.LeftAtReset > LeftWorthSaying ? $"{Math.Round(pace.LeftAtReset):0}% left at the reset · {Day(pace.End)}"
                : $"Lasts to the reset · {Day(pace.End)}";
        }
        else
        {
            line = w.ResetsAt is { } r ? $"Resets {(LimitPacing.Length(w.Name) is { } l && l <= TimeSpan.FromDays(1) ? Time(r) : Day(r))}" : "";
        }

        return new(w.Name, Math.Clamp(w.UsedPercent, 0, 100), PaceMark(w, now), line, warns);
    }

    /// <summary>Where an even pace would be now: elapsed ÷ length × 100, the start being the reset less the length; null when either is not known.</summary>
    public static double? PaceMark(LimitWindow w, DateTime now)
    {
        if (w.ResetsAt is not { } end || LimitPacing.Length(w.Name) is not { } length)
        {
            return null;
        }

        return Math.Clamp((now - (end - length)) / length * 100, 0, 100);
    }

    /// <summary>One app on one line: <c>Claude Code · 5-hour 75%, weekly 66%</c>.</summary>
    public static string Summary(WidgetApp a) => $"{a.Name} · " + string.Join(", ", a.Rows.Select(r => $"{Lower(r.Window)} {Math.Round(r.Used):0}%"));

    /// <summary>The tray tooltip: one line per app; past <see cref="TooltipMax"/> characters the first apps and <c>+N more</c>.</summary>
    public static string Tooltip(IReadOnlyList<WidgetApp> apps)
    {
        if (apps.Count == 0)
        {
            return "AI Stackprint";
        }

        var lines = apps.Select(Summary).ToList();
        for (var n = lines.Count; n > 0; n--)
        {
            var text = string.Join("\n", lines.Take(n)) + (n < lines.Count ? $"\n+{lines.Count - n} more" : "");
            if (text.Length <= TooltipMax)
            {
                return text;
            }
        }

        return lines[0].Length <= TooltipMax ? lines[0] : lines[0][..(TooltipMax - 1)] + "…";
    }

    /// <summary>The app with the most-used window, and that percent; null with no readings.</summary>
    public static (WidgetApp App, double Used)? MostUsed(IEnumerable<WidgetApp> apps) =>
        apps.Where(a => a.Rows.Count > 0).Select(a => (App: a, Used: a.Rows.Max(r => r.Used))).OrderByDescending(t => t.Used).Cast<(WidgetApp, double)?>().FirstOrDefault();

    /// <summary>"5-hour" stays as the provider names it; "Weekly" reads "weekly" inside a sentence.</summary>
    public static string Lower(string window) => window.Length > 0 && char.IsUpper(window[0]) ? char.ToLowerInvariant(window[0]) + window[1..] : window;

    public static string Time(DateTime t) => t.ToString("HH:mm", _en);

    public static string Day(DateTime t) => t.ToString("ddd HH:mm", _en);
}
