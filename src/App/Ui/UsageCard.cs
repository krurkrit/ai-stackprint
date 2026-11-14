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
internal partial class UsageCard(Ctx x, AppDefinition def, UsageCard.Prefs prefs)
{
    /// <summary>What the user picked; kept by the page so it survives rebuilds.</summary>
    public class Prefs { public int Days = 7; public bool Cost; public bool CacheReads; public string Account = ""; }

    public static bool Supports(AppDefinition d) => d.Logs.Concat(d.Usage).Any(l => LogParsers.Find(l.Format) != null);

    private readonly ContentControl _body = new();
    private CancellationTokenSource? _cts;
    private UsageResult? _last;
    private UsageLive? _live;
    private List<string> _accounts = [];
    private DateTime _since;

    public Control Build()
    {
        var range = new ContentControl();
        void Pills() => range.Content = W.Pills([("7 days", "7"), ("30 days", "30"), ("All", "0")], prefs.Days.ToString(), v => { prefs.Days = int.Parse(v); Pills(); Refresh(); });
        Pills();
        var card = Section("data_pie", "Usage", _body, range);
        card.DetachedFromVisualTree += (_, _) => { _cts?.Cancel(); _body.Content = null; _last = null; _live = null; };
        Refresh();
        return card;
    }

    private async void Refresh()
    {
        _cts?.Cancel();
#pragma warning disable S2930 // disposed in the finally below when done or replaced
        var cts = _cts = new CancellationTokenSource();
#pragma warning restore S2930
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
                    files = [.. files.Where(f => (f.Account ?? "Default") == prefs.Account)];
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _body.Content = Txt("Cannot read logs: " + e.Message, TextSize.Small, false, Muted); }
        finally
        {
            if (_cts == cts)
            {
                _cts = null;
            }

            cts.Dispose();   // finished or replaced: a newer refresh has its own
        }
    }

    private void Render()
    {
        if (_last == null)
        {
            return;
        }

        if (_accounts.Count < 2) { _body.Content = Show(_last); return; }
        var pills = Pills(_accounts.Select(a => (a, a)).Prepend(("All accounts", "")), _accounts.Contains(prefs.Account) ? prefs.Account : "", v => { prefs.Account = v; Refresh(); });
        _body.Content = new StackPanel { Spacing = Space.XL, Children = { Row(Txt("Account", TextSize.Small, false, Muted), pills), Show(_last) } };
    }

    private Control Show(UsageResult r)
    {
        var b = r.Buckets;
        if (b.Count == 0)
        {
            return Txt(r.Files == 0 ? "No session logs found for this app." : "No usage in this period.", TextSize.Small, false, Muted);
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
        var models = b.Select(v => v.Model).Distinct().Count();
        bool priced = unpriced.Count < models, writes = cw > 0, cost = priced && prefs.Cost, reads = cr > 0 && prefs.CacheReads;   // hide what the logs or prices cannot fill

        var tiles = new WrapPanel();
        void Tile(string label, string value, string? tip = null)
        {
            var t = Stat(label, value).With(s => s.Margin = new Thickness(0, 0, Space.L, Space.L));
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

        var metric = Pills([("Tokens", "t"), ("Cost", "c")], cost ? "c" : "t", v => { prefs.Cost = v == "c"; Render(); }).With(m => m.IsVisible = priced);
        var cache = new CheckBox { Content = "Include cache reads", IsChecked = prefs.CacheReads, IsVisible = !cost && cr > 0, VerticalAlignment = VerticalAlignment.Center };
        cache.IsCheckedChanged += (_, _) => { prefs.CacheReads = cache.IsChecked == true; Render(); };
        var chart = Chart(b, cost, priced, reads, writes, out var legend);

        var modelRows = b.GroupBy(v => v.Model).Select(g => (Name: g.Key, In: g.Sum(v => v.Input), Out: g.Sum(v => v.Output), CW: g.Sum(v => v.CacheWrite), CR: g.Sum(v => v.CacheRead), Cost: CostOf(g)))
            .OrderByDescending(m => m.In + m.Out + m.CW + m.CR).Take(10)
            .Select(m => new TableRow([.. new Control?[] { Txt(m.Name, TextSize.Small, true), Txt(Fmt(m.In), TextSize.Small), Txt(Fmt(m.Out), TextSize.Small), writes ? Txt(Fmt(m.CW), TextSize.Small) : null, Txt(Fmt(m.CR), TextSize.Small), priced ? Txt(m.Cost is { } c ? Money(c) : "–", TextSize.Small) : null }.OfType<Control>()]));
        var projectRows = b.GroupBy(v => v.Project).Select(g => (Name: g.Key, Msgs: g.Sum(v => v.Messages), Tok: g.Sum(v => v.Input + v.Output + v.CacheWrite), Cost: CostOf(g)))
            .OrderByDescending(p => p.Tok).Take(10)
            .Select(p => new TableRow([.. new Control?[] { Txt(p.Name, TextSize.Small, true), Txt(p.Msgs.ToString("N0"), TextSize.Small), Txt(Fmt(p.Tok), TextSize.Small), priced ? Txt(p.Cost is { } c ? Money(c) : "–", TextSize.Small) : null }.OfType<Control>()]));

        var notes = new StackPanel { Spacing = Space.XS };
        if (unpriced.Count > 0)
        {
            notes.Children.Add(Txt("No price for: " + string.Join(", ", unpriced) + (priced ? ". Cost counts only models with a price." : ".") + " \"Edit prices\" adds an empty entry for each; fill in the numbers.", TextSize.Caption, false, Muted));
        }

        if (r.Capped)
        {
            notes.Children.Add(Txt("Files over 8 MB: only the newest 8 MB of each is counted.", TextSize.Caption, false, Muted));
        }

        if (r.Files >= LogReader.MaxFiles)
        {
            notes.Children.Add(Txt($"Counts the newest {LogReader.MaxFiles} log files.", TextSize.Caption, false, Muted));
        }

        if (priced)
        {
            notes.Children.Add(Txt("Cost is the app's own figure where its log has one, else an estimate from list prices (cache writes at the 5-minute price). Cache read and write are counted apart from input.", TextSize.Caption, false, Muted));
        }

        var edit = Btn("Edit prices", () => { Pricing.Ensure(); Pricing.AddMissing(unpriced); PathUtil.OpenFile(Pricing.FilePath); }, false, "edit").With(e => e.HorizontalAlignment = HorizontalAlignment.Left);

        return new StackPanel
        {
            Spacing = Space.XL,
            Children =
            {
                Limits(),
                tiles,
                Row(metric, cache),
                chart, legend,
                Title("By model", TextSize.Body), Table.Build([.. new[] { new Col("Model", "2*"), new Col("Input", "1*", true), new Col("Output", "1*", true), writes ? new Col("Cache write", "1*", true) : null, new Col("Cache read", "1*", true), priced ? new Col("Est. cost", "1*", true) : null }.OfType<Col>()], modelRows),
                Title("By project", TextSize.Body), Table.Build([.. new[] { new Col("Project", "2*"), new Col("Messages", "1*", true), new Col("Tokens (no cache reads)", "1.4*", true), priced ? new Col("Est. cost", "1*", true) : null }.OfType<Col>()], projectRows),
                notes, edit
            }
        };
    }
}
