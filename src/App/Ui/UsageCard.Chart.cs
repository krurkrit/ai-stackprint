// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

partial class UsageCard
{
    /// <summary>Per-day bars, stacked by token type (or one bar of cost). At most the last 90 days; gaps are empty days.</summary>
    Control Chart(List<UsageBucket> b, bool cost, bool priced, bool reads, bool writes, out Control legend)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var first = prefs.Days == 0 ? b.Min(v => v.Day) : DateOnly.FromDateTime(_since);
        if (today.DayNumber - first.DayNumber > 89)
        {
            first = today.AddDays(-89);
        }

        var byDay = b.Where(v => v.Day >= first).GroupBy(v => v.Day).ToDictionary(g => g.Key, g => g.ToList());
        var keep = cost ? new[] { true } : new[] { true, true, writes, reads };
        var keys = (cost ? new[] { Accent } : new[] { ChartIn, ChartOut, ChartWrite, ChartRead }).Where((_, k) => keep[k]).ToArray();
        var names = (cost ? new[] { "Estimated cost" } : new[] { "Input", "Output", "Cache write", "Cache read" }).Where((_, k) => keep[k]).ToArray();
        legend = new WrapPanel { Children = { } };
        for (int k = 0; k < names.Length; k++)
        {
            ((WrapPanel)legend).Children.Add(DotText(names[k], keys[k]).With(s => s.Margin = new Thickness(0, 0, 14, 0)));
        }

        var bars = new List<(DateOnly Day, double[] Parts, string Tip)>();
        for (var d = first; d <= today; d = d.AddDays(1))
        {
            var list = byDay.GetValueOrDefault(d) ?? new();
            long i = list.Sum(v => v.Input), o = list.Sum(v => v.Output), w = list.Sum(v => v.CacheWrite), c = list.Sum(v => v.CacheRead);
            decimal money = list.Sum(v => Pricing.Cost(v) ?? 0m);
            var parts = (cost ? new[] { (double)money } : new double[] { i, o, w, c }).Where((_, k) => keep[k]).ToArray();
            bars.Add((d, parts, $"{d:yyyy-MM-dd}\nInput {Fmt(i)} · Output {Fmt(o)}\n" + (writes ? $"Cache write {Fmt(w)} · " : "") + $"Cache read {Fmt(c)}" + (priced ? $"\nEst. cost {Money(money)}" : "")));
        }
        double max = bars.Max(v => v.Parts.Sum());
        if (max <= 0)
        {
            return Txt(!cost ? "No tokens in this period." : "No cost to show: the models used in this period have no price.", 13, false, Muted);
        }

        const double barHeight = 140;
        var grid = new Grid { ColumnSpacing = 3, Height = barHeight + 22, Margin = new Thickness(0, 6, 0, 0) };
        int step = Math.Max(1, (int)Math.Ceiling(bars.Count / 8.0));
        for (int i = 0; i < bars.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var col = new Grid { RowDefinitions = new RowDefinitions($"{barHeight},Auto"), Background = Avalonia.Media.Brushes.Transparent };   // transparent: the tooltip works over the whole column
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 28 };
            for (int j = bars[i].Parts.Length - 1; j >= 0; j--)
            {
                if (bars[i].Parts[j] > 0)
                {
                    stack.Children.Add(new Border { Height = Math.Max(1, barHeight * bars[i].Parts[j] / max), CornerRadius = new CornerRadius(1) }.Themed(Border.BackgroundProperty, keys[j]));
                }
            }

            var label = Txt(i % step == 0 ? bars[i].Day.ToString("MM-dd") : "", 10, false, Muted).With(t => { t.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; t.HorizontalAlignment = HorizontalAlignment.Center; });
            Grid.SetRow(label, 1);
            col.Children.Add(stack); col.Children.Add(label);
            ToolTip.SetTip(col, bars[i].Tip);
            Grid.SetColumn(col, i);
            grid.Children.Add(col);
        }
        return grid;
    }

    /// <summary>Limits the app reports (Codex) and use in the last 5 hours / 7 days / current 5-hour block (all apps with usage logs).</summary>
    Control Limits()
    {
        var live = _live;
        if (live == null)
        {
            return new StackPanel();
        }

        var sp = new StackPanel { Spacing = 8 };
        // Apps with live limits show them in the Plan & limits card above
        var limits = LimitsView.Has(def) ? new List<LimitSample>() : live.Limits;
        sp.Children.Add(Title(limits.Count > 0 ? "Limits" : "Recent use", 14));
        foreach (var l in limits)
        {
            bool stale = l.ResetsAt < DateTime.Now;   // window already reset: the reading is old
            double pct = stale ? 0 : Math.Clamp(l.UsedPercent, 0, 100);
            var bar = new Grid { Height = 8, ColumnDefinitions = new ColumnDefinitions() };
            bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(pct, 0.001), GridUnitType.Star));
            bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(100 - pct, 0.001), GridUnitType.Star));
            var fill = new Border { CornerRadius = new CornerRadius(4) }.Themed(Border.BackgroundProperty, pct >= 90 ? ErrorKey : pct >= 75 ? WarningKey : Accent);
            var rest = new Border { CornerRadius = new CornerRadius(4) }.Themed(Border.BackgroundProperty, Subtle);
            Grid.SetColumn(rest, 1);
            bar.Children.Add(fill); bar.Children.Add(rest);
            sp.Children.Add(new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    Row(Txt($"{l.Name} limit", 13, true), Txt(stale ? "window has reset" : $"{l.UsedPercent:0.#}% used · resets {Reset(l.ResetsAt)}", 13, false, Muted)),
                    bar
                }
            });
        }
        static string Win(WindowUse w) => $"{Fmt(w.Tokens)} tokens · {w.Messages:N0} messages" + (w.Cost is { } c ? " · " + Money(c) : "");
        var rows = new List<Control>
        {
            Txt("Last 5 hours: " + Win(live.Last5h), 13), Txt("Last 7 days: " + Win(live.Last7d), 13)
        };
        if (live.Block != null && live.BlockStart is { } bs)
        {
            rows.Add(Txt($"Current 5-hour block: {Win(live.Block)} · started {bs:HH:mm}, ends about {bs.AddHours(5):HH:mm}", 13));
        }

        foreach (var r in rows)
        {
            sp.Children.Add(r);
        }

        sp.Children.Add(Txt(limits.Count > 0 ? "Percent and reset time are what the app wrote in its log; they update when the app runs. Tokens exclude cache reads."
            : "A block starts at the first message after a 5-hour gap. Tokens exclude cache reads.", 12, false, Muted));
        return sp;
    }

    static string Reset(DateTime t) => t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("ddd HH:mm");

    static decimal? CostOf(IEnumerable<UsageBucket> g)
    {
        decimal sum = 0; bool any = false;
        foreach (var v in g)
        {
            if (Pricing.Cost(v) is { } c) { sum += c; any = true; }
        }

        return any ? sum : null;
    }

    static Border Stat(string label, string value) =>
        new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), MinWidth = 110, Child = new StackPanel { Spacing = 2, Children = { Title(value, 18), Txt(label, 12, false, Muted) } } }
            .Themed(Border.BackgroundProperty, Subtle);

    /// <summary>1.2K / 3.4M / 5.6B.</summary>
    internal static string Fmt(long n) => n switch
    {
        >= 1_000_000_000 => (n / 1e9).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "B",
        >= 1_000_000 => (n / 1e6).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (n / 1e3).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K",
        _ => n.ToString()
    };

    internal static string Money(decimal d) => "$" + d.ToString(d < 100 ? "N2" : "N0", System.Globalization.CultureInfo.InvariantCulture);
}
