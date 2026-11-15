// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Usage page: tokens and estimated cost across every app with readable logs (<see cref="UsageReport"/>) for the last 7 or 30 days: totals, a per-day chart stacked by app,
/// a table by app (a row opens the app page) and by model. Read in the background once per scan and period.
/// </summary>
internal class UsageView(Ctx x)
{
    private const int TopApps = 4;   // apps with their own color in the chart; the rest are "Other"
    private static readonly string[] _keys = [Accent, ChartIn, ChartOut, ChartWrite, Muted];

    private int _days = 7;
    private object? _scan;
    private int _cachedDays;
    private List<AppUsage>? _last;
    private ContentControl _host = new();

    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave() => _host = new();

    public Control Build()
    {
        if (!ReferenceEquals(_scan, x.S.Items) || _cachedDays != _days) { _scan = x.S.Items; _cachedDays = _days; _last = null; }
        var head = new DockPanel();
        var pills = Pills([("7 days", "7"), ("30 days", "30")], _days.ToString(), v => { _days = int.Parse(v); x.Refresh(); });
        DockPanel.SetDock(pills, Dock.Right);
        head.Children.Add(pills);
        head.Children.Add(H1("Usage"));
        var intro = Txt("Tokens and estimated cost from the session logs of every app that keeps them. Cost is the app's own figure where its log has one, else an estimate from list prices; edit prices on the Models page.", TextSize.Small, false, Muted);
        if (_last != null)
        {
            _host.Content = Show(_last);
        }
        else
        {
            var defs = x.S.Defs.Where(d => x.ShownApps.Any(a => a.Id == d.Id)).ToList();
            var projects = x.S.Projects.ToList();
            var since = DateTime.Today.AddDays(1 - _days);
            Busy.Into(_host, "Calculating usage from the logs…", ct => UsageReport.Compute(defs, projects, since, ct), r => { _last = r; return Show(r); });
        }

        return Page(head, intro, _host.Free());
    }

    private Control Show(List<AppUsage> apps)
    {
        if (apps.Count == 0)
        {
            return Txt("No usage in this period. Apps that keep session logs (Claude Code, Codex, OpenCode and others) show up here.", TextSize.Small, false, Muted);
        }

        var all = apps.SelectMany(a => a.Result.Buckets).ToList();
        var cost = AppUsage.CostOf(all);
        var tiles = new WrapPanel();
        void Tile(string label, string value) => tiles.Children.Add(UsageCard.Stat(label, value).With(s => s.Margin = new Thickness(0, 0, Space.L, Space.L)));
        Tile("Input", UsageCard.Fmt(all.Sum(b => b.Input)));
        Tile("Output", UsageCard.Fmt(all.Sum(b => b.Output)));
        Tile("Cache read", UsageCard.Fmt(all.Sum(b => b.CacheRead)));
        if (cost is { } c)
        {
            Tile("Est. cost", UsageCard.Money(c));
        }

        if (all.Sum(b => b.WebSearches) is > 0 and var searches)
        {
            Tile("Web searches", searches.ToString("N0"));
        }

        Tile("Sessions", apps.Sum(a => a.Result.Sessions).ToString("N0"));

        var shown = Math.Min(apps.Count, TopApps + 1);
        var legend = new WrapPanel();
        for (var i = 0; i < shown; i++)
        {
            legend.Children.Add(DotText(i < TopApps ? apps[i].App.Name : "Other", _keys[i]).With(s => s.Margin = new Thickness(0, 0, Space.XL, 0)));
        }

        var bars = new List<(DateOnly Day, double[] Parts, string Tip)>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var d = today.AddDays(1 - _days); d <= today; d = d.AddDays(1))
        {
            var parts = new double[shown];
            var lines = new List<string>();
            for (var i = 0; i < apps.Count; i++)
            {
                var t = apps[i].Result.Buckets.Where(b => b.Day == d).Sum(b => b.Input + b.Output + b.CacheWrite);
                parts[Math.Min(i, TopApps)] += t;
                if (t > 0)
                {
                    lines.Add($"{apps[i].App.Name} {UsageCard.Fmt(t)}");
                }
            }

            bars.Add((d, parts, d.ToString("yyyy-MM-dd") + (lines.Count > 0 ? "\n" + string.Join("\n", lines) : "\nNo usage")));
        }

        var chart = bars.Max(b => b.Parts.Sum()) > 0 ? DayBars.Build(bars, _keys[..shown]) : Txt("No tokens in this period.", TextSize.Small, false, Muted);

        var appRows = apps.Select(a => new TableRow(
        [
            AppName(a.App.Logo, a.App.Category, a.App.Name, Sizes.LogoS), Txt(UsageCard.Fmt(a.Tokens), TextSize.Small), Txt(UsageCard.Fmt(a.Result.Buckets.Sum(b => b.CacheRead)), TextSize.Small),
            Txt(a.Cost is { } m ? UsageCard.Money(m) : "–", TextSize.Small), Txt(a.Result.Sessions.ToString("N0"), TextSize.Small)
        ], () => x.Go("App:" + a.App.Id)));
        var modelRows = all.GroupBy(b => b.Model).Select(g => (Name: g.Key, Tok: g.Sum(b => b.Input + b.Output + b.CacheWrite), Read: g.Sum(b => b.CacheRead), Cost: AppUsage.CostOf(g)))
            .OrderByDescending(m => m.Tok).Take(10)
            .Select(m => new TableRow([UsageCard.ModelName(m.Name), Txt(UsageCard.Fmt(m.Tok), TextSize.Small), Txt(UsageCard.Fmt(m.Read), TextSize.Small), Txt(m.Cost is { } v ? UsageCard.Money(v) : "–", TextSize.Small)], () => x.Go(UsageCard.ModelRoute(m.Name))));

        var data = ModelCatalog.Load().Updated;
        var stale = ModelDataUpdate.Stale(data, DateTime.UtcNow)
            ? $" Prices are from model data of {data}, over {ModelDataUpdate.StaleAfter.Days} days old: turn on Settings › Updates, or update AI Stackprint, for newer prices."
            : "";
        var note = Txt("Tokens are input, output and cache writes; cache reads are counted apart. Models without a price are left out of the cost; a model row opens it on the Models page, to add or change its price." + stale, TextSize.Caption, false, Muted);
        return new StackPanel
        {
            Spacing = Space.XL,
            Children =
            {
                tiles, chart, legend,
                Section("data_pie", "By app", Table.Build([new Col("App", "2*"), new Col("Tokens", "1*", true), new Col("Cache read", "1*", true), new Col("Est. cost", "1*", true), new Col("Sessions", "1*", true)], appRows)),
                Section("brain_circuit", "By model", Table.Build([new Col("Model", "2*"), new Col("Tokens", "1*", true), new Col("Cache read", "1*", true), new Col("Est. cost", "1*", true)], modelRows)),
                note
            }
        };
    }
}
