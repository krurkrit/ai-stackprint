// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Usage of one app from its session logs: tokens by type, estimated cost, a per-day chart, and totals by model and by project.
/// Calculated in the background; stops and lets go of its data when the page closes.
/// </summary>
class UsageCard(Ctx x, AppDefinition def, UsageCard.Prefs prefs)
{
    /// <summary>What the user picked; kept by the page so it survives rebuilds.</summary>
    public class Prefs { public int Days = 7; public bool Cost; public bool CacheReads; public string Account = ""; }

    public static bool Supports(AppDefinition d) => d.Logs.Concat(d.Usage).Any(l => LogParsers.Find(l.Format) != null);

    readonly ContentControl _body = new();
    CancellationTokenSource? _cts;
    UsageResult? _last;
    UsageLive? _live;
    List<string> _accounts = new();
    DateTime _since;

    public Control Build()
    {
        var range = new ContentControl();
        void Pills() => range.Content = W.Pills(new[] { ("7 days", "7"), ("30 days", "30"), ("All", "0") }, prefs.Days.ToString(), v => { prefs.Days = int.Parse(v); Pills(); Refresh(); });
        Pills();
        var card = Section("data_pie", "Usage", _body, range);
        card.DetachedFromVisualTree += (_, _) => { _cts?.Cancel(); _body.Content = null; _last = null; _live = null; };
        Refresh();
        return card;
    }

    async void Refresh()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        if (_last == null)
        {
            _body.Content = Busy.Loading("Calculating usage from the logs…");
        }

        var projects = x.S.Projects.ToList(); var d = def; _live = null;
        var since = _since = prefs.Days == 0 ? DateTime.MinValue : DateTime.Today.AddDays(1 - prefs.Days);
        try
        {
            var (r, live, accounts) = await Task.Run(() =>
            {
                var files = LogReader.Find(d, projects, usage: true);
                var names = files.Select(f => f.Account ?? "Default").Distinct().OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
                if (prefs.Account != "" && names.Contains(prefs.Account))
                {
                    files = files.Where(f => (f.Account ?? "Default") == prefs.Account).ToList();
                }

                return (UsageStats.Compute(files, since, cts.Token), UsageStats.Live(files, DateTime.Now, cts.Token), names);
            }, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _last = r; _live = live; _accounts = accounts;
            Render();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _body.Content = Txt("Cannot read logs: " + e.Message, 13, false, Muted); }
    }

    void Render()
    {
        if (_last == null)
        {
            return;
        }

        if (_accounts.Count < 2) { _body.Content = Show(_last); return; }
        var pills = W.Pills(_accounts.Select(a => (a, a)).Prepend(("All accounts", "")), _accounts.Contains(prefs.Account) ? prefs.Account : "", v => { prefs.Account = v; Refresh(); });
        _body.Content = new StackPanel { Spacing = 14, Children = { Row(Txt("Account", 13, false, Muted), pills), Show(_last) } };
    }

    Control Show(UsageResult r)
    {
        var b = r.Buckets;
        if (b.Count == 0)
        {
            return Txt(r.Files == 0 ? "No session logs found for this app." : "No usage in this period.", 13, false, Muted);
        }

        long inp = b.Sum(v => v.Input), outp = b.Sum(v => v.Output), cw = b.Sum(v => v.CacheWrite), cr = b.Sum(v => v.CacheRead);
        int msgs = b.Sum(v => v.Messages), sub = b.Sum(v => v.SubAgentMessages);
        var unpriced = new SortedSet<string>(); decimal total = 0;
        foreach (var v in b)
        {
            if (Pricing.Cost(v) is { } c)
            {
                total += c;
            }
            else
            {
                unpriced.Add(v.Model);
            }
        }
        int models = b.Select(v => v.Model).Distinct().Count();
        bool priced = unpriced.Count < models, writes = cw > 0, cost = priced && prefs.Cost, reads = cr > 0 && prefs.CacheReads;   // hide what the logs or prices cannot fill

        var tiles = new WrapPanel();
        void Tile(string label, string value, string? tip = null)
        {
            var t = Stat(label, value).With(s => s.Margin = new Thickness(0, 0, 10, 10));
            if (tip != null)
            {
                ToolTip.SetTip(t, tip);
            }

            tiles.Children.Add(t);
        }
        Tile("Input", Fmt(inp), "Input tokens that were not served from the cache");
        Tile("Output", Fmt(outp));
        if (writes)
        {
            Tile("Cache write", Fmt(cw));
        }

        Tile("Cache read", Fmt(cr), "Input served from the cache (cheaper)");
        if (priced)
        {
            Tile("Est. cost", Money(total), "The app's own figure where its log has one, else an estimate from prices.json");
        }

        Tile("Sessions", r.Sessions.ToString("N0"));
        Tile("Messages", msgs.ToString("N0"), sub > 0 ? $"{sub:N0} from sub-agents" : null);

        var metric = W.Pills(new[] { ("Tokens", "t"), ("Cost", "c") }, cost ? "c" : "t", v => { prefs.Cost = v == "c"; Render(); }).With(m => m.IsVisible = priced);
        var cache = new CheckBox { Content = "Include cache reads", IsChecked = prefs.CacheReads, IsVisible = !cost && cr > 0, VerticalAlignment = VerticalAlignment.Center };
        cache.IsCheckedChanged += (_, _) => { prefs.CacheReads = cache.IsChecked == true; Render(); };
        var chart = Chart(b, cost, priced, reads, writes, out var legend);

        var modelRows = b.GroupBy(v => v.Model).Select(g => (Name: g.Key, In: g.Sum(v => v.Input), Out: g.Sum(v => v.Output), CW: g.Sum(v => v.CacheWrite), CR: g.Sum(v => v.CacheRead), Cost: CostOf(g)))
            .OrderByDescending(m => m.In + m.Out + m.CW + m.CR).Take(10)
            .Select(m => new TableRow(new Control?[] { Txt(m.Name, 13, true), Txt(Fmt(m.In), 13), Txt(Fmt(m.Out), 13), writes ? Txt(Fmt(m.CW), 13) : null, Txt(Fmt(m.CR), 13), priced ? Txt(m.Cost is { } c ? Money(c) : "–", 13) : null }.OfType<Control>().ToArray()));
        var projectRows = b.GroupBy(v => v.Project).Select(g => (Name: g.Key, Msgs: g.Sum(v => v.Messages), Tok: g.Sum(v => v.Input + v.Output + v.CacheWrite), Cost: CostOf(g)))
            .OrderByDescending(p => p.Tok).Take(10)
            .Select(p => new TableRow(new Control?[] { Txt(p.Name, 13, true), Txt(p.Msgs.ToString("N0"), 13), Txt(Fmt(p.Tok), 13), priced ? Txt(p.Cost is { } c ? Money(c) : "–", 13) : null }.OfType<Control>().ToArray()));

        var notes = new StackPanel { Spacing = 4 };
        if (unpriced.Count > 0)
        {
            notes.Children.Add(Txt("No price for: " + string.Join(", ", unpriced) + (priced ? ". Cost counts only models with a price." : ".") + " \"Edit prices\" adds an empty entry for each; fill in the numbers.", 12, false, Muted));
        }

        if (r.Capped)
        {
            notes.Children.Add(Txt("Files over 8 MB: only the newest 8 MB of each is counted.", 12, false, Muted));
        }

        if (r.Files >= LogReader.MaxFiles)
        {
            notes.Children.Add(Txt($"Counts the newest {LogReader.MaxFiles} log files.", 12, false, Muted));
        }

        if (priced)
        {
            notes.Children.Add(Txt("Cost is the app's own figure where its log has one, else an estimate from list prices (cache writes at the 5-minute price). Cache read and write are counted apart from input.", 12, false, Muted));
        }

        var edit = Btn("Edit prices", () => { Pricing.Ensure(); Pricing.AddMissing(unpriced); PathUtil.OpenFile(Pricing.FilePath); }, false, "edit").With(e => e.HorizontalAlignment = HorizontalAlignment.Left);

        return new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Limits(),
                tiles,
                Row(metric, cache),
                chart, legend,
                Title("By model", 14), Table.Build(new[] { new Col("Model", "2*"), new Col("Input", "1*", true), new Col("Output", "1*", true), writes ? new Col("Cache write", "1*", true) : null, new Col("Cache read", "1*", true), priced ? new Col("Est. cost", "1*", true) : null }.OfType<Col>().ToArray(), modelRows),
                Title("By project", 14), Table.Build(new[] { new Col("Project", "2*"), new Col("Messages", "1*", true), new Col("Tokens (no cache reads)", "1.4*", true), priced ? new Col("Est. cost", "1*", true) : null }.OfType<Col>().ToArray(), projectRows),
                notes, edit
            }
        };
    }

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
