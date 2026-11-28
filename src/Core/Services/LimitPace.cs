// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>A point of the usage line; <paramref name="Measured"/> is false for the reset (0 % by definition) before the first reading.</summary>
public record PacePoint(DateTime At, double Used, bool Measured = true);

/// <summary>
/// The short window (5-hour) inside the long one. <paramref name="Share"/>: how much of the long window one full short window is worth, in percent,
/// worked out from readings where both rose; null until there is enough to tell. <paramref name="FullAt"/>: when it fills at its pace so far, if before it resets.
/// </summary>
public record ShortPace(string Name, DateTime Start, DateTime End, double Used, double? Share, DateTime? FullAt)
{
    /// <summary>The pace line of the short window on the long window's scale: from its start to its end, rising by <see cref="Share"/>.</summary>
    public (double From, double To)? Line(double longUsed) => Share is { } s ? (longUsed - Used / 100 * s, longUsed - Used / 100 * s + s) : null;
}

/// <summary>
/// Pace of the longest limit window (weekly): how much is used against an even pace that reaches 100 % at the reset, the forecast at the recent pace,
/// and the short window inside it.
/// </summary>
public record LimitPace(string Name, DateTime Start, DateTime End, DateTime Now, double Used, List<PacePoint> Points, double PerHour, DateTime? FullAt, double LeftAtReset, ShortPace? Short)
{
    /// <summary>Where an even pace would be now (0–100).</summary>
    public double PaceNow => Math.Clamp((Now - Start) / (End - Start) * 100, 0, 100);

    /// <summary>Percent per day that uses the rest exactly by the reset.</summary>
    public double PerDayToLast => Math.Max(0, 100 - Used) / Math.Max((End - Now).TotalDays, 1 / 24.0);
}

/// <summary>Works out <see cref="LimitPace"/> from the latest reading and the history (<see cref="LimitHistory"/>).</summary>
public static partial class LimitPacing
{
    /// <summary>The recent pace is taken over the last 24 hours of readings; with less than 3 hours of them, over the whole window so far.</summary>
    private static readonly TimeSpan _recent = TimeSpan.FromHours(24), _minSpan = TimeSpan.FromHours(3), _sameReset = TimeSpan.FromHours(1);

    /// <summary>Short-window rises (in percent) needed before its share of the long window is trusted.</summary>
    private const double MinShortRise = 10;

    /// <summary>How long a window named by a provider lasts; null when the name does not say.</summary>
    public static TimeSpan? Length(string name) => name switch
    {
        "5-hour" => TimeSpan.FromHours(5),
        "Daily" => TimeSpan.FromDays(1),
        _ when name.StartsWith("Weekly", StringComparison.Ordinal) => TimeSpan.FromDays(7),
        _ when Minutes().Match(name) is { Success: true } m => TimeSpan.FromMinutes(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)),
        _ => null
    };

    [GeneratedRegex(@"^(\d+)-minute$")]
    private static partial Regex Minutes();

    /// <summary>The pace of <paramref name="latest"/>'s longest window with a known length and reset (the plain "Weekly" first); null when it has none.</summary>
    public static LimitPace? Compute(LimitSnapshot latest, IReadOnlyList<LimitReading> history, DateTime now)
    {
        var timed = latest.Windows.Where(w => w.ResetsAt != null && Length(w.Name) != null).ToList();
        var main = timed.FirstOrDefault(w => w.Name == "Weekly") ?? timed.MaxBy(w => Length(w.Name));
        if (main == null || main.ResetsAt <= now)
        {
            return null;
        }

        var length = Length(main.Name)!.Value;
        DateTime end = main.ResetsAt!.Value, start = end - length;
        var readings = history.Append(new LimitReading(latest.FetchedAt, latest.Windows)).Where(r => r.At >= start && r.At <= now).OrderBy(r => r.At).ToList();
        var points = readings.Select(r => Find(r, main.Name, end) is { } w ? new PacePoint(r.At, w.UsedPercent) : null).OfType<PacePoint>()
            .DistinctBy(p => p.At).ToList();
        var used = main.UsedPercent;
        if (points.Count == 0 || points[^1].At < now)
        {
            points.Add(new(now, used));
        }

        if (points[0].At > start)
        {
            points.Insert(0, new(start, 0, false));
        }

        var measured = points.Where(p => p.Measured).ToList();
        var from = measured.FirstOrDefault(p => p.At >= now - _recent) ?? measured[0];
        var perHour = now - from.At >= _minSpan ? (used - from.Used) / (now - from.At).TotalHours : used / Math.Max((now - start).TotalHours, 1 / 60.0);
        perHour = Math.Max(0, perHour);
        DateTime? fullAt = perHour > 0 && used < 100 && now.AddHours((100 - used) / perHour) is var f && f < end ? f : used >= 100 ? now : null;
        var left = Math.Max(0, 100 - used - perHour * (end - now).TotalHours);
        var shortWin = timed.Where(w => w != main && Length(w.Name) < length).MinBy(w => Length(w.Name));
        return new(main.Name, start, end, now, used, points, perHour, fullAt, left, shortWin == null ? null : Short(shortWin, main, readings, now));
    }

    private static ShortPace Short(LimitWindow w, LimitWindow main, List<LimitReading> readings, DateTime now)
    {
        var length = Length(w.Name)!.Value;
        DateTime end = w.ResetsAt!.Value, start = end - length;
        var used = end <= now ? 0 : w.UsedPercent;
        if (end <= now)
        {
            (start, end) = (now, now + length);   // it has reset and no new window has started: the next starts with the next use
        }

        DateTime? full = null;
        if (used is > 0 and < 100 && now - start >= TimeSpan.FromMinutes(10))
        {
            var at = start.AddHours(100 * (now - start).TotalHours / used);
            full = at < end ? at : null;
        }
        else if (used >= 100)
        {
            full = now;
        }

        // Its share of the long window: over pairs of readings in the same short and long windows where the short one rose
        double rise = 0, longRise = 0;
        for (var i = 1; i < readings.Count; i++)
        {
            if (Pair(readings[i - 1], readings[i], w.Name) is { } s && Pair(readings[i - 1], readings[i], main.Name) is { } l && s.Rise > 0 && l.Rise >= 0)
            {
                rise += s.Rise;
                longRise += l.Rise;
            }
        }

        return new(w.Name, start, end, used, rise >= MinShortRise ? longRise / rise * 100 : null, full);
    }

    /// <summary>How much a window rose between two readings, if both are of the same window (the same reset, give or take an hour).</summary>
    private static (double Rise, DateTime Reset)? Pair(LimitReading a, LimitReading b, string name)
    {
        var wa = a.Windows.FirstOrDefault(w => w.Name == name);
        var wb = b.Windows.FirstOrDefault(w => w.Name == name);
        return wa?.ResetsAt is { } ra && wb?.ResetsAt is { } rb && (ra - rb).Duration() < _sameReset ? (wb.UsedPercent - wa.UsedPercent, rb) : null;
    }

    private static LimitWindow? Find(LimitReading r, string name, DateTime end) =>
        r.Windows.FirstOrDefault(w => w.Name == name && w.ResetsAt is { } x && (x - end).Duration() < _sameReset);
}
