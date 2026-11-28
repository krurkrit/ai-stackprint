// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class UsageCard
{
    /// <summary>Per-day bars, stacked by token type (or one bar of cost). At most the last 90 days; gaps are empty days.</summary>
    private static readonly string[] _costKeys = [ChartKey(1)], _tokenKeys = [ChartIn, ChartOut, ChartWrite, ChartRead];
    private static readonly string[] _costNames = ["Estimated cost"], _tokenNames = ["Input", "Output", "Cache write", "Cache read"];

    private Control Chart(List<UsageBucket> b, bool cost, bool priced, bool reads, bool writes, out Control legend)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var first = prefs.Days == 0 ? b.Min(v => v.Day) : DateOnly.FromDateTime(_since);
        if (today.DayNumber - first.DayNumber > 89)
        {
            first = today.AddDays(-89);
        }

        var byDay = b.Where(v => v.Day >= first).GroupBy(v => v.Day).ToDictionary(g => g.Key, g => g.ToList());
        var keep = cost ? new[] { true } : [true, true, writes, reads];
        var keys = (cost ? _costKeys : _tokenKeys).Where((_, k) => keep[k]).ToArray();
        var names = (cost ? _costNames : _tokenNames).Where((_, k) => keep[k]).ToArray();
        legend = new WrapPanel { Children = { } };
        for (var k = 0; k < names.Length; k++)
        {
            ((WrapPanel)legend).Children.Add(DotText(names[k], keys[k]).With(s => s.Margin = new Thickness(0, 0, Space.XL, 0)));
        }

        var bars = new List<(DateOnly Day, double[] Parts, string Tip)>();
        for (var d = first; d <= today; d = d.AddDays(1))
        {
            var list = byDay.GetValueOrDefault(d) ?? [];
            long i = list.Sum(v => v.Input), o = list.Sum(v => v.Output), w = list.Sum(v => v.CacheWrite), c = list.Sum(v => v.CacheRead);
            var money = list.Sum(v => ModelCatalog.Cost(v) ?? 0m);
            var parts = (cost ? new[] { (double)money } : [i, o, w, c]).Where((_, k) => keep[k]).ToArray();
            bars.Add((d, parts, $"{d:yyyy-MM-dd}\nInput {Fmt(i)} · Output {Fmt(o)}\n" + (writes ? $"Cache write {Fmt(w)} · " : "") + $"Cache read {Fmt(c)}" + (priced ? $"\nEst. cost {Money(money)}" : "")));
        }
        var max = bars.Max(v => v.Parts.Sum());
        if (max <= 0)
        {
            return Txt(!cost ? "No tokens in this period." : "No cost to show: the models used in this period have no price.", TextSize.Small, false, Muted);
        }

        return DayBars.Build(bars, keys);
    }

    /// <summary>Limits the app reports (Codex) and use in the last 5 hours / 7 days / current 5-hour block (all apps with usage logs).</summary>
    private StackPanel Limits()
    {
        var live = _live;
        if (live == null)
        {
            return new StackPanel();
        }

        var sp = new StackPanel { Spacing = Space.M };
        // Apps with live limits show them in the Plan & limits card above
        var limits = LimitsView.Has(def) ? [] : live.Limits;
        sp.Children.Add(Title(limits.Count > 0 ? "Limits" : "Recent use", TextSize.Body));
        foreach (var l in limits)
        {
            var stale = l.ResetsAt < DateTime.Now;   // window already reset: the reading is old
            var pct = stale ? 0 : Math.Clamp(l.UsedPercent, 0, 100);
            var bar = new Grid
            {
                Height = Sizes.BarThin,
                ColumnDefinitions = []
            };
            bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(pct, 0.001), GridUnitType.Star));
            bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(100 - pct, 0.001), GridUnitType.Star));
            var fill = new Border { CornerRadius = new CornerRadius(Radius.S) }.Themed(Border.BackgroundProperty, pct >= 90 ? ErrorMark : pct >= 75 ? WarningMark : Accent);
            var rest = new Border { CornerRadius = new CornerRadius(Radius.S) }.Themed(Border.BackgroundProperty, Subtle);
            Grid.SetColumn(rest, 1);
            bar.Children.Add(fill);
            bar.Children.Add(rest);
            sp.Children.Add(new StackPanel
            {
                Spacing = Space.XS,
                Children =
                {
                    Row(Txt($"{l.Name} limit", TextSize.Small, true), Txt(stale ? "window has reset" : $"{l.UsedPercent:0.#}% used · resets {Reset(l.ResetsAt)}", TextSize.Small, false, Muted)),
                    bar
                }
            });
        }
        static string Win(WindowUse w) => $"{Fmt(w.Tokens)} tokens · {w.Messages:N0} messages" + (w.Cost is { } c ? " · " + Money(c) : "");
        var rows = new List<Control>
        {
            Txt("Last 5 hours: " + Win(live.Last5h), TextSize.Small), Txt("Last 7 days: " + Win(live.Last7d), TextSize.Small)
        };
        if (live.Block != null && live.BlockStart is { } bs)
        {
            rows.Add(Txt($"Current 5-hour block: {Win(live.Block)} · started {bs:HH:mm}, ends about {bs.AddHours(5):HH:mm}", TextSize.Small));
        }

        foreach (var r in rows)
        {
            sp.Children.Add(r);
        }

        sp.Children.Add(Txt(limits.Count > 0 ? "Percent and reset time are what the app wrote in its log; they update when the app runs. Tokens exclude cache reads."
            : "A block starts at the first message after a 5-hour gap. Tokens exclude cache reads.", TextSize.Caption, false, Muted));
        return sp;
    }

    private static string Reset(DateTime t) => t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("ddd HH:mm");

    private static decimal? CostOf(IEnumerable<UsageBucket> g)
    {
        decimal sum = 0;
        var any = false;
        foreach (var v in g)
        {
            if (ModelCatalog.Cost(v) is { } c)
            {
                sum += c;
                any = true;
            }
        }

        return any ? sum : null;
    }

    internal static Border Stat(string label, string value) =>
        new Border
        {
            CornerRadius = new CornerRadius(Radius.L),
            Padding = new Thickness(Space.XL, Space.L),
            MinWidth = Widths.FieldS,
            Child = new StackPanel
            {
                Spacing = Space.XXS,
                Children = { Title(value, TextSize.Title), Txt(label, TextSize.Caption, false, Muted) }
            }
        }
            .Themed(Border.BackgroundProperty, Subtle);

    /// <summary>1.2K / 3.4M / 5.6B.</summary>
    internal static string Fmt(long n) => n switch
    {
        >= 1_000_000_000 => (n / 1e9).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "B",
        >= 1_000_000 => (n / 1e6).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (n / 1e3).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K",
        _ => n.ToString()
    };

    /// <summary>A logged model name, with the model-data entry its price comes from when that differs ("priced as …"); no price says so.</summary>
    internal static Control ModelName(string name)
    {
        var key = ModelCatalog.PricedAs(name);
        var note = key == null ? "no price" : !string.Equals(key, name, StringComparison.OrdinalIgnoreCase) ? "priced as " + key : null;
        var t = Txt(name, TextSize.Small, true);
        ToolTip.SetTip(t, "Open on the Models page");
        return note == null ? t : new StackPanel { Children = { t, Txt(note, TextSize.Caption, false, Muted) } };
    }

    /// <summary>The Models page with this model's entry (the one it is priced as), or adding it when it has none.</summary>
    internal static string ModelRoute(string name) => "Models:" + (ModelCatalog.PricedAs(name) ?? name.ToLowerInvariant());

    internal static string Money(decimal d) => "$" + d.ToString(d < 100 ? "N2" : "N0", System.Globalization.CultureInfo.InvariantCulture);
}
