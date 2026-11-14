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
internal static partial class DashboardView
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
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = Space.XL, RowSpacing = Space.XL };
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

        var charts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = Space.XL };
        var left = new StackPanel { Spacing = Space.XL, Children = { Section("sparkle", "Skills by app", ByApp(x, "Skill")), Section("puzzle_piece", "Plugins by app", ByApp(x, "Plugin")), Section("wrench_screwdriver", "What plugins add", PluginContents(x)) } };
        var right = new StackPanel { Spacing = Space.XL, Children = { Section("people", "Skills and plugins by author", ByAuthor(x)), Recent(x) } };
        Grid.SetColumn(right, 1);
        charts.Children.Add(left); charts.Children.Add(right);
        return Page(head, tiles, LimitsView.Strip(x), charts);
    }

    /// <summary>Installed apps by category as a donut, with a legend that also lists categories with nothing installed ("0 of 3").</summary>
    private static Border Categories(List<AppInfo> apps, Action open)
    {
        var cats = apps.GroupBy(a => a.Category).OrderByDescending(g => g.Count(a => a.Installed)).ThenBy(g => g.Key).ToList();
        var inst = apps.Count(a => a.Installed);
        var pie = new PieChart("categories", [.. cats.Select(g => ((double)g.Count(a => a.Installed), CategoryKey(g.Key), $"{AppsView.Cap(g.Key)}: {g.Count(a => a.Installed)} of {g.Count()}"))], inst, "installed");
        var legend = new StackPanel { Spacing = Space.M, VerticalAlignment = VerticalAlignment.Center };
        foreach (var g in cats)
        {
            var n = g.Count(a => a.Installed);
            var key = CategoryKey(g.Key);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = Space.L, Background = Brushes.Transparent };
            row.PointerEntered += (_, _) => pie.Highlight(key);   // the legend row points at its slice
            row.PointerExited += (_, _) => pie.Highlight(null);
            row.Children.Add(Dot(CategoryKey(g.Key)));
            row.Children.Add(Txt(AppsView.Cap(g.Key), TextSize.Small, n > 0, n > 0 ? Text : Muted).With(t => Grid.SetColumn(t, 1)));
            row.Children.Add(Txt($"{n} of {g.Count()}", TextSize.Small, false, Muted).With(t => Grid.SetColumn(t, 2)));
            legend.Children.Add(row);
        }
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = Space.XXXL, HorizontalAlignment = HorizontalAlignment.Center };
        legend.MinWidth = Widths.Legend;
        body.Children.Add(pie.View); body.Children.Add(legend.With(l => Grid.SetColumn(l, 1)));
        // Scales down (never up) when the card is narrower than donut + legend, instead of the legend running under the next cards
        var fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = body, VerticalAlignment = VerticalAlignment.Center };
        var card = Section("apps", "Apps by category", fit);
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
            return Txt(kind == "Skill" ? "No skills found." : "No plugins found.", TextSize.Small, false, Muted);
        }

        var max = groups.Max(g => g.Count());
        var sp = new StackPanel { Spacing = Space.M };
        foreach (var g in groups)
        {
            var d = x.S.Def(g.Key);
            var segs = Levels.Order.Select(l => (g.Count(i => i.Level == l), LevelKey(l))).ToArray();
            sp.Children.Add(Bar(AppName(d?.Logo, d?.Category ?? "custom", d?.Name ?? g.Key, Sizes.LogoS), segs, g.Count(), max, () => x.Browse(kind, g.Key, null),
                string.Join("\n", Levels.Order.Select(l => (l, n: g.Count(i => i.Level == l))).Where(t => t.n > 0).Select(t => $"{t.l}: {t.n}"))));
        }
        sp.Children.Add(new WrapPanel().With(w => { foreach (var l in Levels.Order.Where(l => groups.Any(g => g.Any(i => i.Level == l)))) { w.Children.Add(DotText(l, LevelKey(l)).With(t => t.Margin = new Thickness(0, Space.XS, Space.XL, 0))); } }));
        return sp;
    }

    /// <summary>
    /// Top authors of skills and plugins (stacked: skills, plugins), the rest as "Other". The author is the plugin manifest's author, the skill's author or
    /// metadata.author, else the plugin's marketplace; skills with none are "Local" (yours) or the app's name when they ship with it.
    /// </summary>
    private static Control ByAuthor(Ctx x)
    {
        var items = x.S.Items.Where(i => i.Kind is "Skill" or "Plugin").ToList();
        var author = ItemAuthor.For(items);   // a plugin's skills count for the plugin's author
        if (items.Count == 0)
        {
            return Txt("No skills or plugins found.", TextSize.Small, false, Muted);
        }

        var groups = items.GroupBy(author).OrderByDescending(g => g.Count()).ToList();
        var top = groups.Take(TopAuthors).Select(g => (Name: g.Key, Skills: g.Count(i => i.Kind == "Skill"), Plugins: g.Count(i => i.Kind == "Plugin"), Other: false)).ToList();
        if (groups.Count > TopAuthors)
        {
            top.Add(("Other", groups.Skip(TopAuthors).Sum(g => g.Count(i => i.Kind == "Skill")), groups.Skip(TopAuthors).Sum(g => g.Count(i => i.Kind == "Plugin")), true));
        }

        var max = top.Max(t => t.Skills + t.Plugins);
        var sp = new StackPanel { Spacing = Space.M };
        foreach (var (name, skills, plugins, other) in top)
        {
            sp.Children.Add(Bar(Txt(name, TextSize.Small, true).With(b => b.TextTrimming = TextTrimming.CharacterEllipsis), [(skills, ChartIn), (plugins, ChartOut)], skills + plugins, max,
                other ? null : () => x.Browse(skills >= plugins ? "Skill" : "Plugin", null, $"author:\"{name}\""), $"{name}\nSkills: {skills}\nPlugins: {plugins}"));
        }

        sp.Children.Add(Row(DotText("Skills", ChartIn), DotText("Plugins", ChartOut)).With(r => r.Margin = new Thickness(0, Space.XS, 0, 0)));
        return sp;
    }

    /// <summary>Label | stacked bar scaled to the largest row | count. Clickable when <paramref name="open"/> is set.</summary>
    private static Control Bar(Control label, (int N, string Key)[] segs, int total, int max, Action? open, string tip)
    {
        var bar = new Grid { Height = Sizes.Bar, ColumnSpacing = Space.Hair, VerticalAlignment = VerticalAlignment.Center };
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

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Sizes.LabelColumn},*,{Sizes.NumberColumn}"), ColumnSpacing = Space.L, Background = Brushes.Transparent };
        g.Children.Add(label.With(l => l.VerticalAlignment = VerticalAlignment.Center));
        g.Children.Add(new Border { CornerRadius = new CornerRadius(Radius.S), ClipToBounds = true, Child = bar, VerticalAlignment = VerticalAlignment.Center }.With(b => Grid.SetColumn(b, 1)));
        g.Children.Add(Txt(total.ToString("N0"), TextSize.Small, false, Muted).With(t => { Grid.SetColumn(t, 2); t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center; }));
        ToolTip.SetTip(g, tip);
        if (open == null)
        {
            return g;
        }

        var row = new Border { CornerRadius = new CornerRadius(Radius.M), Padding = new Thickness(Space.S, Space.XS), Cursor = Hand, Background = Brushes.Transparent, Child = g };
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
            body = Txt("Changes show here from the next scan on.", TextSize.Small, false, Muted);
        }
        else if (c.Changes.Count == 0)
        {
            body = Txt("Nothing was added, removed or edited.", TextSize.Small, false, Muted);
        }
        else
        {
            var rows = c.Changes.OrderBy(ch => ch.What).ThenBy(ch => ch.Name, StringComparer.OrdinalIgnoreCase).Take(12).Select(ch => new TableRow(
            [
                Chip(ch.What, ch.What switch { "Added" => SuccessKey, "Removed" => ErrorKey, _ => WarningKey }), Txt(ch.Kind == "Config" ? "Config file" : ch.Kind, TextSize.Small, false, Muted),
                Txt(ch.Name, TextSize.Small, true), Txt(x.S.Def(ch.App)?.Name ?? ch.App, TextSize.Small, false, Muted), Txt(ch.Detail ?? "", TextSize.Caption, false, Muted)
            ], ch.What == "Removed" ? null : ch.Kind == "MCP server" ? () => x.Go("Mcp") : () => { if (x.S.Items.FirstOrDefault(i => i.Path == ch.Path && i.Name == ch.Name) is { } it) { x.Open(it); } }));
            body = new StackPanel { Spacing = Space.S, Children = { Table.Build([new Col("", "100"), new Col("", ".9*"), new Col("Name", "1.6*"), new Col("App", "1.2*"), new Col("", "1.6*")], rows) } };
            if (c.Changes.Count > 12)
            {
                ((StackPanel)body).Children.Add(Txt($"… and {c.Changes.Count - 12} more", TextSize.Caption, false, Muted));
            }
        }
        return Section("arrow_sync", title, body);
    }

    private static Border Tile(string icon, string title, string big, Action open, params (string label, int n, string key)[] segs)
    {
        var grid = new Grid { Height = Sizes.Bar, ColumnSpacing = Space.XXS };
        var legend = new WrapPanel { ItemSpacing = Space.XL, LineSpacing = Space.XS };
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
                legend.Children.Add(DotText($"{label} {n}", key).With(d => d.TextWrapping = TextWrapping.Wrap));   // a narrow tile wraps instead of cutting off the count
            }
        }
        if (col == 0)
        {
            grid.Children.Add(new Border().Themed(Border.BackgroundProperty, BorderKey));
        }

        var card = Section(icon, title, new StackPanel
        {
            Spacing = Space.L,
            Children = { Title(big, TextSize.Display).With(t => { t.FontFeatures = _tabular; t.HorizontalAlignment = HorizontalAlignment.Right; }),
                new Border { CornerRadius = new CornerRadius(Radius.S), ClipToBounds = true, Child = grid }, legend }
        });
        card.Cursor = Hand;
        card.PointerReleased += (_, _) => open();
        return card;
    }

    /// <summary>Tabular figures: numbers line up.</summary>
    private static readonly FontFeatureCollection _tabular = [FontFeature.Parse("tnum")];
}
