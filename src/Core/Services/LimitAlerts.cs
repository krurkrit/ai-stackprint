// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

using AiStackprint.Models;

namespace AiStackprint.Services;

public enum AlertKind { ShortNearlyFull, RunsOutEarly, Reset, UsageLeft }

/// <param name="Key">Sent at most once per key (<see cref="UiState.AlertsSent"/>).</param>
/// <param name="Open">The alert has an Open button (the app's page); <paramref name="Pause"/>: and "Pause alerts for 1 hour".</param>
public record Alert(AlertKind Kind, string Key, string AppId, string Title, string Text, bool Open, bool Pause);

/// <summary>
/// Decides which limit alerts to send after a reading: a short window (5-hour) crossing a threshold upward, the long window (weekly) running out early,
/// the long window resetting, and usage left before its reset. Each fires at most once per key; keys older than <see cref="KeepKeys"/> are dropped.
/// While alerts are paused, the alerts that would fire are marked sent, not queued.
/// </summary>
public sealed class LimitAlerts
{
    public static readonly TimeSpan KeepKeys = TimeSpan.FromDays(8);
    private static readonly TimeSpan _sameReset = TimeSpan.FromHours(1);
    private static readonly CultureInfo _en = CultureInfo.InvariantCulture;
    private readonly Dictionary<string, List<LimitWindow>> _last = [];

    /// <summary>The alerts to send now; <paramref name="sent"/> gets their keys (and loses old ones). <paramref name="changed"/>: <paramref name="sent"/> changed and should be saved.</summary>
    public List<Alert> Check(string appId, string app, LimitSnapshot now, IReadOnlyList<LimitReading> history, Settings s, IDictionary<string, DateTime> sent,
        DateTime? pausedUntilUtc, DateTime nowLocal, DateTime nowUtc, out bool changed)
    {
        changed = false;
        foreach (var old in sent.Where(k => nowUtc - k.Value > KeepKeys).Select(k => k.Key).ToList())
        {
            sent.Remove(old);
            changed = true;
        }

        var prev = _last.GetValueOrDefault(appId) ?? history.LastOrDefault(r => r.At < now.FetchedAt.AddSeconds(-1))?.Windows;
        _last[appId] = now.Windows;
        if (!s.AlertsEnabled)
        {
            return [];
        }

        var pace = LimitPacing.Compute(now, history, nowLocal);
        var due = new List<Alert>();
        Short(appId, app, now, prev, pace, s, due);
        if (pace != null)
        {
            Long(appId, app, pace, nowLocal, s, due);
        }

        if (s.AlertReset && pace != null && prev?.FirstOrDefault(w => w.Name == pace.Name) is { ResetsAt: { } oldReset } before && (pace.End - oldReset).Duration() >= _sameReset)
        {
            var name = WidgetRows.Lower(pace.Name);
            due.Add(new(AlertKind.Reset, $"reset|{appId}|{pace.Name}|{Hour(oldReset)}", appId, $"{app}: {name} limit reset",
                $"You used {Math.Round(before.UsedPercent):0}% {(pace.Name.StartsWith("Weekly", StringComparison.Ordinal) ? "last week" : "in the last window")}. Next reset {WidgetRows.Day(pace.End)}.", false, false));
        }

        var send = due.Where(a => !sent.ContainsKey(a.Key)).ToList();
        foreach (var a in send)
        {
            sent[a.Key] = nowUtc;
            changed = true;
        }

        return pausedUntilUtc is { } p && p > nowUtc ? [] : send;
    }

    /// <summary>A short window crossing a threshold upward since the last reading (a new window starts at 0). Only the highest one crossed is sent.</summary>
    private static void Short(string appId, string app, LimitSnapshot now, List<LimitWindow>? prev, LimitPace? pace, Settings s, List<Alert> due)
    {
        foreach (var w in now.Windows.Where(w => w.ResetsAt != null && LimitPacing.Length(w.Name) is { } l && l < TimeSpan.FromDays(1)))
        {
            var before = prev?.FirstOrDefault(p => p.Name == w.Name && p.ResetsAt is { } r && (r - w.ResetsAt!.Value).Duration() < _sameReset)?.UsedPercent ?? 0;
            var crossed = s.AlertShortAt.Where(t => t > 0 && w.UsedPercent >= t && before < t).DefaultIfEmpty(0).Max();
            if (crossed == 0)
            {
                continue;
            }

            var reset = w.ResetsAt!.Value;
            var full = pace?.Short is { } sh && sh.Name == w.Name && sh.FullAt is { } f && f < reset ? f : (DateTime?)null;
            due.Add(new(AlertKind.ShortNearlyFull, $"short|{appId}|{w.Name}|{Hour(reset)}|{crossed}", appId, $"{app}: {w.Name} limit at {Math.Round(w.UsedPercent):0}%",
                full is { } at ? $"Full about {WidgetRows.Time(at)} at this pace. It resets at {WidgetRows.Time(reset)}." : $"It resets at {WidgetRows.Time(reset)}.", true, true));
        }
    }

    private static void Long(string appId, string app, LimitPace pace, DateTime nowLocal, Settings s, List<Alert> due)
    {
        var name = WidgetRows.Lower(pace.Name);
        if (s.AlertEarlyHours > 0 && pace.FullAt is { } full && pace.End - full >= TimeSpan.FromHours(s.AlertEarlyHours))
        {
            due.Add(new(AlertKind.RunsOutEarly, $"early|{appId}|{pace.Name}|{nowLocal:yyyyMMdd}", appId, $"{app}: {name} limit runs out early",
                $"At this pace it's reached {WidgetRows.Day(full)}, {Span(pace.End - full)} before the reset. Slow to {PerDay(pace)}% a day to last.", true, false));
        }

        var left = pace.End - nowLocal;
        if (s.AlertLeftHours > 0 && left > TimeSpan.Zero && left <= TimeSpan.FromHours(s.AlertLeftHours) && pace.LeftAtReset > s.AlertLeftPercent)
        {
            due.Add(new(AlertKind.UsageLeft, $"left|{appId}|{pace.Name}|{Hour(pace.End)}", appId, $"{app}: {Math.Round(pace.LeftAtReset):0}% left before the reset",
                $"It resets {WidgetRows.Day(pace.End)}. You can use about {PerDay(pace)}% a day.", true, false));
        }
    }

    private static string PerDay(LimitPace p) => p.PerDayToLast.ToString("0.0", _en);

    /// <summary>"2 d 15 h", "15 h" or "40 min".</summary>
    public static string Span(TimeSpan t) => t.TotalHours >= 24 ? $"{(int)t.TotalDays} d {t.Hours} h" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} h" : $"{Math.Max(1, t.Minutes)} min";

    /// <summary>A reset time rounded to the hour: providers' reset times move by seconds between readings.</summary>
    private static string Hour(DateTime t) => t.AddMinutes(30).ToString("yyyyMMddHH", _en);
}
