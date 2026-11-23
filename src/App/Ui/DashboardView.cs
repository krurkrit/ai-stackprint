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

    public static Control Build(Ctx x)
    {
        var s = x.S;
        var apps = x.ShownApps.ToList();
        var head = Head();
        head.Children.Add(H1("Dashboard"));

        var all = s.AllHealth.ToList();
        var warn = all.Count(h => h.Finding.Warning);
        var tiles = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = Space.XL,
            RowSpacing = Space.XL
        };
        Grid.SetIsSharedSizeScope(tiles, true);
        foreach (var row in tiles.RowDefinitions)
        {
            row.SharedSizeGroup = "tiles";   // both rows as tall as the taller one: Plugins, Skills, Servers and Health match
        }
        var cards = new[]
        {
            Tile("puzzle_piece", "Plugins", Count(x, "Plugin").ToString("N0"), () => x.Go("Plugin"), ByLevel(x, "Plugin")),
            Tile("sparkle", "Skills", Count(x, "Skill").ToString("N0"), () => x.Go("Skill"), ByInvoked(x)),
            Tile("server", "Servers", s.Mcp.Count.ToString("N0"), () => x.Go("Mcp"), [.. Levels.Order.Select(l => (l, s.Mcp.Count(m => m.Level == l), LevelKey(l)))]),
            s.HealthReady ? Tile("heart_pulse", "Health", warn.ToString("N0"), () => x.Go("Health"), ("Warnings", warn, WarningKey), ("Guidelines", all.Count - warn, ChartRead)) : HealthPending(x)
        };
        var radar = Categories(apps, () => x.Go("Apps"));
        Grid.SetRowSpan(radar, 2);
        Grid.SetColumnSpan(radar, 2);   // 2 x 2 tiles on the left; the four tiles fill the right
        tiles.Children.Add(radar);
        for (var i = 0; i < cards.Length; i++)
        {
            Grid.SetColumn(cards[i], 2 + i % 2);
            Grid.SetRow(cards[i], i / 2);
            tiles.Children.Add(cards[i]);
        }

        var charts = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = Space.XL
        };
        var left = new StackPanel
        {
            Spacing = Space.XL,
            Children = { Section("puzzle_piece", "Plugins by app", ByApp(x, "Plugin")), Section("wrench_screwdriver", "What plugins add", PluginContents(x)), Section("sparkle", "Skills by app", ByApp(x, "Skill")) }
        };
        var right = new StackPanel
        {
            Spacing = Space.XL,
            Children = { Section("people", "Plugins and skills by author", ByAuthor(x)), Recent(x) }
        };
        Grid.SetColumn(right, 1);
        charts.Children.Add(left);
        charts.Children.Add(right);
        return Page(head, tiles, LimitsView.Strip(x), charts);
    }

    /// <summary>What kinds of AI apps are installed: the installed apps by category as a donut, with the count of each (apps that are not installed do not count).</summary>
    private static Border Categories(List<AppInfo> apps, Action open)
    {
        var cats = apps.Where(a => a.Installed).GroupBy(a => a.Category).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        if (cats.Count == 0)
        {
            return Section("apps", "Apps by category", Txt("No AI apps found on this computer.", TextSize.Small, false, Muted));
        }

        var inst = cats.Sum(g => g.Count());
        var pie = new PieChart("categories", [.. cats.Select(g => ((double)g.Count(), CategoryKey(g.Key), $"{AppsView.Cap(g.Key)}: {g.Count()}"))], inst, inst == 1 ? "app" : "apps");
        var legend = new StackPanel
        {
            Spacing = Space.M,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var g in cats)
        {
            var n = g.Count();
            var key = CategoryKey(g.Key);
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = Space.L,
                Background = Brushes.Transparent
            };
            row.PointerEntered += (_, _) => pie.Highlight(key);   // the legend row points at its slice
            row.PointerExited += (_, _) => pie.Highlight(null);
            row.Children.Add(Dot(CategoryKey(g.Key)));
            row.Children.Add(Txt(AppsView.Cap(g.Key), TextSize.Small, true).With(t => Grid.SetColumn(t, 1)));
            row.Children.Add(Txt(n.ToString("N0"), TextSize.Small, false, Muted).With(t => Grid.SetColumn(t, 2)));
            legend.Children.Add(row);
        }
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = Space.XXXL,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        legend.MinWidth = Widths.Legend;
        body.Children.Add(pie.View);
        body.Children.Add(legend.With(l => Grid.SetColumn(l, 1)));
        // Scales down (never up) when the card is narrower than donut + legend, instead of the legend running under the next cards
        var fit = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Child = body,
            VerticalAlignment = VerticalAlignment.Center
        };
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
}
