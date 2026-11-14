// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Overview: tiles that open their page, live limits, skills and plugins by app and by author (a bar opens the list filtered to it),
/// and what changed since the scan before. The full app list is on the Apps page.
/// </summary>
internal static class DashboardView
{
    private const int TopAuthors = 10;

    public static Control Build(Ctx x, Action addProject)
    {
        var s = x.S;
        var apps = x.ShownApps.ToList();
        var head = new DockPanel();
        var btns = Row(Btn("Add project folder", addProject, false, "folder_add"), Btn("Rescan", x.Rescan, true, "arrow_sync"));
        DockPanel.SetDock(btns, Dock.Right);
        head.Children.Add(btns);
        head.Children.Add(H1("Dashboard"));

        int inst = apps.Count(a => a.Installed), warn = s.Health.Count(h => h.Finding.Warning);
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 16, RowSpacing = 16 };
        var cards = new[]
        {
            Tile("apps", "Apps installed", inst.ToString("N0"), () => x.Go("Apps"), ("Installed", inst, SuccessKey), ("Not found", apps.Count - inst, ErrorKey)),
            Tile("puzzle_piece", "Plugins", Count(x, "Plugin").ToString("N0"), () => x.Go("Plugin"), ByLevel(x, "Plugin")),
            Tile("sparkle", "Skills", Count(x, "Skill").ToString("N0"), () => x.Go("Skill"), ByInvoked(x)),
            Tile("warning", "Health warnings", warn.ToString("N0"), () => x.Go("Health"), ("Warnings", warn, WarningKey), ("Guidelines", s.Health.Count - warn, ChartRead))
        };
        var radar = Categories(apps, () => x.Go("Apps"));
        Grid.SetRowSpan(radar, 2); Grid.SetColumnSpan(radar, 2);   // 2 x 2 tiles on the left; the four tiles fill the right
        tiles.Children.Add(radar);
        for (var i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], 2 + i % 2); Grid.SetRow(cards[i], i / 2); tiles.Children.Add(cards[i]); }

        var charts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        var left = new StackPanel { Spacing = 16, Children = { Section("sparkle", "Skills by app", ByApp(x, "Skill")), Section("puzzle_piece", "Plugins by app", ByApp(x, "Plugin")) } };
        var right = new StackPanel { Spacing = 16, Children = { Section("people", "Skills and plugins by author", ByAuthor(x)), Recent(x) } };
        Grid.SetColumn(right, 1);
        charts.Children.Add(left); charts.Children.Add(right);
        return Page(head, tiles, LimitsView.Strip(x), charts);
    }

    /// <summary>Installed apps by category as a donut, with a legend that also lists categories with nothing installed ("0 of 3").</summary>
    private static Border Categories(List<AppInfo> apps, Action open)
    {
        var cats = apps.GroupBy(a => a.Category).OrderByDescending(g => g.Count(a => a.Installed)).ThenBy(g => g.Key).ToList();
        var inst = apps.Count(a => a.Installed);
        var pie = PieChart.Build(cats.Select(g => ((double)g.Count(a => a.Installed), CategoryKey(g.Key))).ToList(), inst.ToString("N0"), "installed");
        var legend = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var g in cats)
        {
            var n = g.Count(a => a.Installed);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(Dot(CategoryKey(g.Key)));
            row.Children.Add(Txt(AppsView.Cap(g.Key), 13, n > 0, n > 0 ? Text : Muted).With(t => Grid.SetColumn(t, 1)));
            row.Children.Add(Txt($"{n} of {g.Count()}", 13, false, Muted).With(t => Grid.SetColumn(t, 2)));
            legend.Children.Add(row);
        }
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 28, HorizontalAlignment = HorizontalAlignment.Center };
        legend.MinWidth = 180;
        body.Children.Add(pie); body.Children.Add(legend.With(l => Grid.SetColumn(l, 1)));
        // Scales down (never up) when the card is narrower than donut + legend, instead of the legend running under the next cards
        var fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = body };
        var card = Section("apps", "Apps by category", new Border { Padding = new Thickness(0, 12), Child = fit });
        card.Cursor = Hand;
        card.PointerReleased += (_, _) => open();
        return card;
    }

    private static int Count(Ctx x, string kind) => x.S.Items.Count(i => i.Kind == kind);

    private static (string, int, string)[] ByLevel(Ctx x, string kind) =>
        [.. Levels.Order.Select(l => (l, x.S.Items.Count(i => i.Kind == kind && i.Level == l), LevelKey(l)))];

    /// <summary>Skills by who can start them, as on the Skills page (without the "All" choice).</summary>
    private static (string, int, string)[] ByInvoked(Ctx x)
    {
        var skills = x.S.Items.Where(i => i.Kind == "Skill").ToList();
        return [.. new[] { ("Agent only", ChartIn), ("User only", ChartOut), ("Agent and User", ChartRead) }.Select(t => (t.Item1, skills.Count(i => SkillMatch.Label(i) == t.Item1), t.Item2))];
    }

    /// <summary>One bar per app, stacked by level (highest first); click opens the list for that app.</summary>
    private static Control ByApp(Ctx x, string kind)
    {
        var groups = x.S.Items.Where(i => i.Kind == kind).GroupBy(i => i.App).OrderByDescending(g => g.Count()).ToList();
        if (groups.Count == 0)
        {
            return Txt(kind == "Skill" ? "No skills found." : "No plugins found.", 13, false, Muted);
        }

        var max = groups.Max(g => g.Count());
        var sp = new StackPanel { Spacing = 8 };
        foreach (var g in groups)
        {
            var d = x.S.Def(g.Key);
            var segs = Levels.Order.Select(l => (g.Count(i => i.Level == l), LevelKey(l))).ToArray();
            sp.Children.Add(Bar(AppName(d?.Logo, d?.Category ?? "custom", d?.Name ?? g.Key, 22), segs, g.Count(), max, () => x.Browse(kind, g.Key, null),
                string.Join("\n", Levels.Order.Select(l => (l, n: g.Count(i => i.Level == l))).Where(t => t.n > 0).Select(t => $"{t.l}: {t.n}"))));
        }
        sp.Children.Add(new WrapPanel().With(w => { foreach (var l in Levels.Order.Where(l => groups.Any(g => g.Any(i => i.Level == l)))) { w.Children.Add(DotText(l, LevelKey(l)).With(t => t.Margin = new Thickness(0, 4, 14, 0))); } }));
        return sp;
    }

    /// <summary>
    /// Top authors of skills and plugins (stacked: skills, plugins), the rest as "Other". The author is the plugin manifest's author, the skill's author or
    /// metadata.author, else the plugin's marketplace; skills with none are "Local" (yours) or the app's name when they ship with it.
    /// </summary>
    private static Control ByAuthor(Ctx x)
    {
        var items = x.S.Items.Where(i => i.Kind is "Skill" or "Plugin").ToList();
        var pluginAuthor = items.Where(i => i.Kind == "Plugin" && i.Author != null).GroupBy(i => (i.App, i.Name)).ToDictionary(g => g.Key, g => g.First().Author!);
        // A plugin's skills count for the plugin's author
        string Author(Item i) => i.Author ?? (i.Plugin is { } p ? pluginAuthor.GetValueOrDefault((i.App, p)) ?? (p.Contains('@') ? p[(p.IndexOf('@') + 1)..] : p) : i.Bundled ? i.Source : "Local");
        if (items.Count == 0)
        {
            return Txt("No skills or plugins found.", 13, false, Muted);
        }

        var groups = items.GroupBy(Author).OrderByDescending(g => g.Count()).ToList();
        var top = groups.Take(TopAuthors).Select(g => (Name: g.Key, Skills: g.Count(i => i.Kind == "Skill"), Plugins: g.Count(i => i.Kind == "Plugin"), Other: false)).ToList();
        if (groups.Count > TopAuthors)
        {
            top.Add(("Other", groups.Skip(TopAuthors).Sum(g => g.Count(i => i.Kind == "Skill")), groups.Skip(TopAuthors).Sum(g => g.Count(i => i.Kind == "Plugin")), true));
        }

        var max = top.Max(t => t.Skills + t.Plugins);
        var sp = new StackPanel { Spacing = 8 };
        foreach (var (name, skills, plugins, other) in top)
        {
            sp.Children.Add(Bar(Txt(name, 13, true).With(b => b.TextTrimming = TextTrimming.CharacterEllipsis), [(skills, ChartIn), (plugins, ChartOut)], skills + plugins, max,
                other ? null : () => x.Browse(skills >= plugins ? "Skill" : "Plugin", null, name), $"{name}\nSkills: {skills}\nPlugins: {plugins}"));
        }

        sp.Children.Add(Row(DotText("Skills", ChartIn), DotText("Plugins", ChartOut)).With(r => r.Margin = new Thickness(0, 4, 0, 0)));
        return sp;
    }

    /// <summary>Label | stacked bar scaled to the largest row | count. Clickable when <paramref name="open"/> is set.</summary>
    private static Control Bar(Control label, (int N, string Key)[] segs, int total, int max, Action? open, string tip)
    {
        var bar = new Grid { Height = 12, ColumnSpacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var col = 0;
        foreach (var (n, key) in segs.Where(s => s.N > 0))
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(n, GridUnitType.Star));
            bar.Children.Add(new Border().Themed(Border.BackgroundProperty, key).With(b => Grid.SetColumn(b, col++)));
        }
        if (max > total)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(max - total, GridUnitType.Star));
        }

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*,48"), ColumnSpacing = 10, Background = Brushes.Transparent };
        g.Children.Add(label.With(l => l.VerticalAlignment = VerticalAlignment.Center));
        g.Children.Add(new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = bar, VerticalAlignment = VerticalAlignment.Center }.With(b => Grid.SetColumn(b, 1)));
        g.Children.Add(Txt(total.ToString("N0"), 13, false, Muted).With(t => { Grid.SetColumn(t, 2); t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; }));
        ToolTip.SetTip(g, tip);
        if (open == null)
        {
            return g;
        }

        var row = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4), Cursor = Hand, Background = Brushes.Transparent, Child = g };
        row.PointerEntered += (_, _) => row.Themed(Border.BackgroundProperty, Subtle);
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, _) => open();
        return row;
    }

    /// <summary>Skills, plugins and config files added, removed or edited since the scan before (saved between runs).</summary>
    private static Border Recent(Ctx x)
    {
        var c = x.S.Changes;
        var title = c.Since is { } since ? $"Changed since {(since.Date == DateTime.Today ? since.ToString("HH:mm") : since.ToString("yyyy-MM-dd HH:mm"))}" : "Changes";
        Control body;
        if (c.Since == null)
        {
            body = Txt("Changes show here from the next scan on.", 13, false, Muted);
        }
        else if (c.Changes.Count == 0)
        {
            body = Txt("Nothing was added, removed or edited.", 13, false, Muted);
        }
        else
        {
            var rows = c.Changes.OrderBy(ch => ch.What).ThenBy(ch => ch.Name, StringComparer.OrdinalIgnoreCase).Take(12).Select(ch => new TableRow(
            [
                Chip(ch.What, ch.What switch { "Added" => SuccessKey, "Removed" => ErrorKey, _ => WarningKey }), Txt(ch.Kind == "Config" ? "Config file" : ch.Kind, 13, false, Muted),
                Txt(ch.Name, 13, true), Txt(x.S.Def(ch.App)?.Name ?? ch.App, 13, false, Muted)
            ], ch.What == "Removed" ? null : () => { if (x.S.Items.FirstOrDefault(i => i.Path == ch.Path && i.Name == ch.Name) is { } it) { x.Open(it); } }));
            body = new StackPanel { Spacing = 6, Children = { Table.Build([new Col("", "100"), new Col("", ".9*"), new Col("Name", "1.6*"), new Col("App", "1.2*")], rows) } };
            if (c.Changes.Count > 12)
            {
                ((StackPanel)body).Children.Add(Txt($"… and {c.Changes.Count - 12} more", 12, false, Muted));
            }
        }
        return Section("arrow_sync", title, body);
    }

    private static Border Tile(string icon, string title, string big, Action open, params (string label, int n, string key)[] segs)
    {
        var grid = new Grid { Height = 10, ColumnSpacing = 2 };
        var legend = new WrapPanel();
        var col = 0;
        foreach (var (label, n, key) in segs)
        {
            if (n > 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(n, GridUnitType.Star)));
                grid.Children.Add(new Border().Themed(Border.BackgroundProperty, key).With(b => Grid.SetColumn(b, col++)));
            }
            if (n > 0)
            {
                legend.Children.Add(DotText($"{label} {n}", key).With(d => { d.Margin = new Thickness(0, 0, 14, 0); d.TextWrapping = TextWrapping.Wrap; }));   // a narrow tile wraps instead of cutting off the count
            }
        }
        if (col == 0)
        {
            grid.Children.Add(new Border().Themed(Border.BackgroundProperty, BorderKey));
        }

        var card = Card(new StackPanel
        {
            Spacing = 10,
            Children = { Beside(IconTile(icon), Txt(title, 13, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center)),
                Title(big, 32).With(t => { t.FontFeatures = _tabular; t.HorizontalAlignment = HorizontalAlignment.Right; }),
                new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = grid }, legend }
        });
        card.Cursor = Hand;
        card.PointerReleased += (_, _) => open();
        return card;
    }

    /// <summary>Tabular figures: numbers line up.</summary>
    private static readonly FontFeatureCollection _tabular = [FontFeature.Parse("tnum")];
}
