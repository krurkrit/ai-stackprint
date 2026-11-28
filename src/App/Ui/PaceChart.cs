// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>
/// The pace chart of a limit window (<see cref="LimitPace"/>): percent used from its start to its reset, the even pace (100 % at the reset),
/// the short window's pace inside it, and the forecast at the recent pace. Under the pointer a line shows the time, the pace and the use there.
/// </summary>
internal sealed class PaceChart : Control
{
    private const double Left = 40, Right = 8, Top = 16, Bottom = 26, LabelGap = 6, EndDot = 4.5, FullDot = 4, BandOpacity = 0.10, NowOpacity = 0.5, Top100 = 110;
    private const double TipPad = 8, TipGap = 12;

    /// <summary>Height for a width (the example's 680 × 300), between <see cref="Heights.Chart"/> and <see cref="MaxHeightFor"/>.</summary>
    private const double Aspect = 300.0 / 680, MaxHeightFor = 360;

    /// <summary>
    /// Line colors: your use in chart slot 1, and its forecast in the same color but dotted (it is your use, carried on); the short window's pace
    /// in the warning mark color (chosen: it is the limit you hit first), with its window as a band of it; the weekly even pace in the muted gray
    /// of the "Now" line (a guide, not a series).
    /// </summary>
    public static readonly string UseKey = W.ChartKey(1), ForecastKey = W.ChartKey(1);
    public const string PaceKey = W.Muted, ShortKey = W.WarningMark;

    /// <summary>Dash patterns (in line widths, so the pace dashes are the same length in pixels at 1 px) of the even pace, the short window's pace and the forecast; the legend draws the same.</summary>
    public static readonly double[] PaceDash = [6, 4], ShortDash = [2, 2], ForecastDash = [0.5, 2];

    /// <summary>The pace lines (weekly and short window) are guides: thin, under the 2 px lines of your use and its forecast.</summary>
    public const double PaceWidth = Stroke.Thin;

    private readonly LimitPace _p;
    private double? _hover;

    public PaceChart(LimitPace pace)
    {
        _p = pace;
        ClipToBounds = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Avalonia.Automation.AutomationProperties.SetName(this, $"{pace.Name} use: {pace.Used:0}% used, even pace {pace.PaceNow:0}%, resets {pace.End:ddd HH:mm}");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? Widths.Legend * 2 : availableSize.Width;
        return new(w, Math.Clamp(w * Aspect, Heights.Chart, MaxHeightFor));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        _hover = x >= Left && x <= Bounds.Width - Right ? x : null;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var b) && b is IBrush brush ? brush : Brushes.Gray;

    private double X(DateTime t) => Left + (t - _p.Start) / (_p.End - _p.Start) * (Bounds.Width - Left - Right);
    private DateTime T(double x) => _p.Start + (x - Left) / (Bounds.Width - Left - Right) * (_p.End - _p.Start);
    private double Y(double pct) => Unclamped(Math.Clamp(pct, 0, Top100));

    /// <summary>The height of a percent past the plot too, for a line that keeps its slope and is clipped at the edges.</summary>
    private double Unclamped(double pct) => Top + (1 - pct / Top100) * (Bounds.Height - Top - Bottom);

    private FormattedText Text(string s, string key, bool bold = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Ui.Theme.Font, FontStyle.Normal, bold ? W.Emphasis : W.Body), W.Size(TextSize.Caption), Brush(key));

    public override void Render(DrawingContext context)
    {
        var c = context;
        if (Bounds.Width <= Left + Right)
        {
            return;
        }

        var grid = new Pen(Brush(W.BorderKey), Stroke.Thin);
        double bottom = Y(0), right = Bounds.Width - Right;
        for (var p = 0; p <= 100; p += 25)
        {
            c.DrawLine(grid, new Point(Left, Y(p)), new Point(right, Y(p)));
            var t = Text($"{p}%", W.Muted);
            c.DrawText(t, new Point(Left - LabelGap - t.Width, Y(p) - t.Height / 2));
        }

        Ticks(c, bottom);
        var shortName = Short(c);
        c.DrawLine(new Pen(Brush(PaceKey), PaceWidth, new DashStyle(PaceDash, 0)), new Point(X(_p.Start), Y(0)), new Point(X(_p.End), Y(100)));

        // Forecast: from now at the recent pace, to the limit or the reset
        var end = _p.FullAt ?? _p.End;
        var at = _p.Used + _p.PerHour * (end - _p.Now).TotalHours;
        c.DrawLine(new Pen(Brush(ForecastKey), Stroke.Medium, new DashStyle(ForecastDash, 0), PenLineCap.Round), new Point(X(_p.Now), Y(_p.Used)), new Point(X(end), Y(Math.Min(at, 100))));
        Rect? limit = null;
        if (_p.FullAt is { } full && full < _p.End)
        {
            c.DrawEllipse(Brush(W.WarningMark), new Pen(Brush(W.Surface), Stroke.Medium), new Point(X(full), Y(100)), FullDot, FullDot);
            var t = Text($"Limit {full:ddd HH:mm}", W.WarningKey, true);
            var at100 = new Point(Math.Clamp(X(full) - t.Width / 2, Left, right - t.Width), Y(100) - t.Height - LabelGap);
            c.DrawText(t, at100);
            limit = new Rect(at100, new Size(t.Width, t.Height));
        }

        ResetLabel(c, shortName, limit);

        Use(c);
        ShortLine(c);
        var now = new Pen(Brush(W.Muted), Stroke.Thin);
        using (c.PushOpacity(NowOpacity))
        {
            c.DrawLine(now, new Point(X(_p.Now), Top), new Point(X(_p.Now), bottom));
        }

        var nowText = Text("Now", W.Muted);
        c.DrawText(nowText, new Point(Math.Min(X(_p.Now) + LabelGap / 2, right - nowText.Width), bottom - nowText.Height - LabelGap / 2));
        c.DrawEllipse(Brush(UseKey), new Pen(Brush(W.Surface), Stroke.Medium), new Point(X(_p.Now), Y(_p.Used)), EndDot, EndDot);
        Hover(c, bottom);
    }

    /// <summary>Day names under each day of a long window (hours for a short one).</summary>
    private void Ticks(DrawingContext c, double bottom)
    {
        var pen = new Pen(Brush(W.BorderKey), Stroke.Thin);
        var days = (_p.End - _p.Start).TotalDays >= 2;
        var step = days ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
        var t = days ? _p.Start.Date.AddDays(1) : _p.Start.Date.AddHours(_p.Start.Hour + 1);
        for (; t < _p.End; t += step)
        {
            c.DrawLine(pen, new Point(X(t), bottom), new Point(X(t), bottom + LabelGap / 2));
            var mid = days ? t.AddHours(12) : t;
            if (mid < _p.End)
            {
                var label = Text(days ? t.ToString("ddd", CultureInfo.CurrentCulture) : t.ToString("HH:mm", CultureInfo.CurrentCulture), W.Muted);
                c.DrawText(label, new Point(X(mid) - label.Width / 2, bottom + LabelGap / 2));
            }
        }
    }

    /// <summary>
    /// "Reset" at the end of the weekly even pace (100 % at the reset, the right end): above it, or under it when that would leave the chart or
    /// cover another label (the 5-hour window's name, the "Limit" time). It sits under the 5-hour name, so the two never run together.
    /// </summary>
    private void ResetLabel(DrawingContext c, params Rect?[] others)
    {
        var t = Text("Reset", W.Muted);
        var y = Y(100);
        var above = new Rect(Bounds.Width - Right - t.Width, y - t.Height - LabelGap / 2, t.Width, t.Height);
        var spot = above.Top < 0 || others.Any(o => o is { } r && r.Intersects(above)) ? above.WithY(y + LabelGap / 2) : above;
        c.DrawText(t, spot.TopLeft);
    }

    /// <summary>The short window as a band, named above it; returns where the name is.</summary>
    private Rect? Short(DrawingContext c)
    {
        if (_p.Short is not { } s)
        {
            return null;
        }

        double x0 = Math.Max(X(s.Start), Left), x1 = Math.Min(X(s.End), Bounds.Width - Right);
        using (c.PushOpacity(BandOpacity))
        {
            c.FillRectangle(Brush(ShortKey), new Rect(x0, Top, Math.Max(0, x1 - x0), Y(0) - Top));
        }

        var name = Text(s.Name, W.Muted);
        var at = new Point((x0 + x1 - name.Width) / 2, Top - name.Height);
        c.DrawText(name, at);
        return new Rect(at, new Size(name.Width, name.Height));
    }

    /// <summary>
    /// The short window's pace line (its whole quota used by its reset), over your use so it shows where both run together. It runs on one window
    /// before and one after the band at the same slope (three band widths in all), so it stands out from the band; it stops at the plot's edges.
    /// </summary>
    private void ShortLine(DrawingContext c)
    {
        if (_p.Short is not { } s || s.Line(_p.Used) is not { } line)
        {
            return;
        }

        var rise = line.To - line.From;
        var span = s.End - s.Start;
        using (c.PushClip(new Rect(Left, Top, Bounds.Width - Left - Right, Y(0) - Top)))
        {
            c.DrawLine(new Pen(Brush(ShortKey), PaceWidth, new DashStyle(ShortDash, 0)),
                new Point(X(s.Start - span), Unclamped(line.From - rise)), new Point(X(s.End + span), Unclamped(line.To + rise)));
        }
    }

    /// <summary>Your use: the readings joined up; the stretch from the reset to the first reading (not measured) is thin.</summary>
    private void Use(DrawingContext c)
    {
        var pts = _p.Points;
        if (pts.Count > 1 && !pts[0].Measured)
        {
            c.DrawLine(new Pen(Brush(UseKey), Stroke.Thin, new DashStyle([2, 2], 0)), new Point(X(pts[0].At), Y(pts[0].Used)), new Point(X(pts[1].At), Y(pts[1].Used)));
        }

        var measured = pts.Where(p => p.Measured).ToList();
        if (measured.Count < 2)
        {
            return;
        }

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(X(measured[0].At), Y(measured[0].Used)), false);
            foreach (var p in measured.Skip(1))
            {
                g.LineTo(new Point(X(p.At), Y(p.Used)));
            }

            g.EndFigure(false);
        }

        c.DrawGeometry(null, new Pen(Brush(UseKey), Stroke.Medium, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
    }

    /// <summary>Under the pointer: a line, and a box with the time, the even pace, and the use there (or the forecast after now).</summary>
    private void Hover(DrawingContext c, double bottom)
    {
        if (_hover is not { } x)
        {
            return;
        }

        var t = T(x);
        c.DrawLine(new Pen(Brush(W.Muted), Stroke.Thin), new Point(x, Top), new Point(x, bottom));
        var pace = Math.Clamp((t - _p.Start) / (_p.End - _p.Start) * 100, 0, 100);
        var lines = new List<string> { t.ToString("ddd HH:mm", CultureInfo.CurrentCulture), $"Even pace {pace:0}%" };
        if (t <= _p.Now)
        {
            lines.Add($"Used {UsedAt(t):0}%");
        }
        else
        {
            lines.Add($"Forecast {Math.Min(100, _p.Used + _p.PerHour * (t - _p.Now).TotalHours):0}%");
        }

        var texts = lines.Select((s, i) => Text(s, W.Text, i == 0)).ToList();
        double w = texts.Max(v => v.Width) + TipPad * 2, h = texts.Sum(v => v.Height) + TipPad * 2;
        var left = x + TipGap + w > Bounds.Width ? x - TipGap - w : x + TipGap;
        var box = new Rect(left, Top, w, h);
        c.DrawRectangle(Brush(W.Surface), new Pen(Brush(W.BorderKey), Stroke.Thin), box, Radius.M, Radius.M);
        var y = Top + TipPad;
        foreach (var v in texts)
        {
            c.DrawText(v, new Point(left + TipPad, y));
            y += v.Height;
        }
    }

    /// <summary>Use at a time: between the two readings around it, on a straight line.</summary>
    private double UsedAt(DateTime t)
    {
        var pts = _p.Points;
        for (var i = 1; i < pts.Count; i++)
        {
            if (pts[i].At >= t)
            {
                var span = (pts[i].At - pts[i - 1].At).TotalSeconds;
                return span <= 0 ? pts[i].Used : pts[i - 1].Used + (pts[i].Used - pts[i - 1].Used) * (t - pts[i - 1].At).TotalSeconds / span;
            }
        }

        return _p.Used;
    }
}
