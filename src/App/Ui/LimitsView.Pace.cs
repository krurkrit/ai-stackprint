// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class LimitsView
{
    /// <summary>Points over or under the even pace that still count as on pace.</summary>
    private const double OnPace = 5;

    /// <summary>Use left at the reset worth pointing out.</summary>
    private const double Leftover = 10;

    /// <summary>
    /// The pace of the longest window (weekly) on the app page: three cards (the weekly window against an even pace, the short 5-hour window,
    /// and where this pace ends up), a legend of the line styles, and the chart. Drawn from the readings kept in <see cref="LimitHistory"/>;
    /// null when no window has a known length and reset.
    /// </summary>
    internal static Control? Pace(Ctx x, AppDefinition d, LimitSnapshot s)
    {
        if (LimitPacing.Compute(s, x.Limits.History.Readings(d.Id), DateTime.Now) is not { } p)
        {
            return null;
        }

        var sp = new StackPanel { Spacing = Space.L, Margin = new Thickness(0, Space.M, 0, 0) };
        var cards = new List<Control>();
        var ahead = p.Used - p.PaceNow;
        cards.Add(Stat($"{p.Name} limit", $"{p.Used:0}% used",
            Math.Abs(ahead) <= OnPace ? (Mark.Good, $"On the even pace ({p.PaceNow:0}% by now)")
            : ahead > 0 ? (Mark.Warning, $"{ahead:0}% over the even pace ({p.PaceNow:0}% by now)")
            : (Mark.Info, $"{-ahead:0}% under the even pace ({p.PaceNow:0}% by now)")));
        if (p.Short is { } sh)
        {
            cards.Add(Stat($"{sh.Name} limit", $"{sh.Used:0}% used", sh.FullAt is { } f
                ? (Mark.Warning, $"Over its pace: full about {f:HH:mm}, resets {Reset(sh.End)}")
                : (Mark.Good, $"Under its pace; resets {Reset(sh.End)}")));
        }

        cards.Add(p.FullAt is { } full
            ? Stat("At this pace", $"Limit {full:ddd HH:mm}", (Mark.Warning, $"{Span(p.End - full)} before the reset. Slow to {p.PerDayToLast:0.#}% a day to last."))
            : p.LeftAtReset > Leftover
                ? Stat("At this pace", $"{p.LeftAtReset:0}% left", (Mark.Info, $"Unused at the reset ({p.End:ddd HH:mm}). You can use about {p.PerDayToLast:0.#}% a day."))
                : Stat("At this pace", "Lasts to the reset", (Mark.Good, $"About {p.LeftAtReset:0}% left at the reset ({p.End:ddd HH:mm}).")));
        var row = new Grid { ColumnSpacing = Space.L };
        foreach (var c in cards)
        {
            Grid.SetColumn(c, row.ColumnDefinitions.Count);
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.Children.Add(c);
        }

        sp.Children.Add(row);
        var legend = new WrapPanel();
        legend.Children.Add(Key($"Your {p.Name.ToLowerInvariant()} use", PaceChart.UseKey, null));
        legend.Children.Add(Key($"{p.Name} pace (100% at the reset)", PaceChart.PaceKey, PaceChart.PaceDash, width: PaceChart.PaceWidth));
        if (p.Short is { Share: not null } withLine)
        {
            legend.Children.Add(Key($"{withLine.Name} pace (current window)", PaceChart.ShortKey, PaceChart.ShortDash, width: PaceChart.PaceWidth));
        }

        legend.Children.Add(Key("Forecast", PaceChart.ForecastKey, PaceChart.ForecastDash, round: true));
        sp.Children.Add(legend);
        sp.Children.Add(new PaceChart(p));
        var note = $"Drawn from the readings taken while AI Stackprint is open (every {x.Limits.Interval.TotalMinutes:0} min)" + (s.FromLogs ? " and those the app wrote in its log" : "") + ".";
        if (p.Short is { Share: null } sh2)
        {
            note += $" The {sh2.Name} pace line shows once the {sh2.Name} limit has risen 10% between readings.";
        }

        sp.Children.Add(Txt(note, TextSize.Caption, false, Muted));
        return sp;
    }

    private enum Mark { Good, Info, Warning }

    /// <summary>A stat card: a label, the figure, and one line with a mark that says how it stands.</summary>
    private static Border Stat(string label, string value, (Mark Mark, string Text) state)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = Space.S };
        line.Children.Add((state.Mark switch
        {
            Mark.Good => new Border { Width = IconSize.S, Height = IconSize.S, Child = Dot(SuccessMark).With(m => m.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center) },   // as tall as an icon, so it sits on the first line
            Mark.Info => Icon("info", IconSize.S).Themed(TextElement.ForegroundProperty, Accent),
            _ => Icon("warning", IconSize.S).Themed(TextElement.ForegroundProperty, WarningMark)
        }).With(m => m.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top));
        line.Children.Add(Txt(state.Text, TextSize.Small).With(t =>
        {
            t.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            Grid.SetColumn(t, 1);
        }));
        return new Border
        {
            CornerRadius = new CornerRadius(Radius.M),
            Padding = new Thickness(Space.L),
            Child = new StackPanel
            {
                Spacing = Space.XS,
                Children = { Txt(label, TextSize.Small, false, Muted), Txt(value, TextSize.Title, true), line }
            }
        }.Themed(Border.BackgroundProperty, Subtle);
    }

    /// <summary>A legend entry: a short piece of the line as the chart draws it, then its name.</summary>
    private static Control Key(string name, string key, double[]? dash, bool round = false, double width = Stroke.Medium)
    {
        var swatch = new Avalonia.Controls.Shapes.Line
        {
            StartPoint = new Point(0, Sizes.Dot / 2),
            EndPoint = new Point(Sizes.LogoS, Sizes.Dot / 2),
            Width = Sizes.LogoS,
            Height = Sizes.Dot,
            StrokeThickness = width,
            StrokeLineCap = round ? Avalonia.Media.PenLineCap.Round : Avalonia.Media.PenLineCap.Flat,
            StrokeDashArray = dash == null ? null : [.. dash],
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        }.Themed(Avalonia.Controls.Shapes.Shape.StrokeProperty, key);
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = Space.S,
            Margin = new Thickness(0, 0, Space.XL, Space.XS),
            Children = { swatch, Txt(name, TextSize.Small, false, Muted) }
        };
    }

    private static string Span(TimeSpan t) => t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{Math.Max(1, t.Minutes)} min";
}
