// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class DashboardView
{
    /// <summary>Label | stacked bar scaled to the largest row | count. Clickable when <paramref name="open"/> is set.</summary>
    private static Control Bar(Control label, (int N, string Key)[] segs, int total, int max, Action? open, string tip)
    {
        var bar = new Grid
        {
            Height = Sizes.Bar,
            ColumnSpacing = Space.Hair,
            VerticalAlignment = VerticalAlignment.Center
        };
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

        var g = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Sizes.LabelColumn},*,{Sizes.NumberColumn}"),
            ColumnSpacing = Space.L,
            Background = Brushes.Transparent
        };
        g.Children.Add(label.With(l => l.VerticalAlignment = VerticalAlignment.Center));
        g.Children.Add(new Border
        {
            Child = bar,
            VerticalAlignment = VerticalAlignment.Center
        }.With(b => Grid.SetColumn(b, 1)));
        g.Children.Add(Txt(total.ToString("N0"), TextSize.Small, false, Muted).With(t =>
        {
            Grid.SetColumn(t, 2);
            t.HorizontalAlignment = HorizontalAlignment.Right;
            t.VerticalAlignment = VerticalAlignment.Center;
        }));
        ToolTip.SetTip(g, tip);
        // The same padding with or without a click, so a row that opens nothing ("Other") lines up with the others
        var row = new Border
        {
            CornerRadius = new CornerRadius(Radius.M),
            Padding = new Thickness(Space.S, Space.XS),
            Background = Brushes.Transparent,
            Child = g
        };
        if (open == null)
        {
            return row;
        }

        row.Cursor = Hand;
        row.Hover();
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
                Chip(ch.What, ch.What switch { "Added" => SuccessMark, "Removed" => ErrorMark, _ => WarningMark }), Txt(ch.Kind == "Config" ? "Config file" : ch.Kind, TextSize.Small, false, Muted),
                Txt(ch.Name, TextSize.Small, true), Txt(x.S.Def(ch.App)?.Name ?? ch.App, TextSize.Small, false, Muted), Txt(ch.Detail ?? "", TextSize.Caption, false, Muted)
            ], ch.What == "Removed" ? null : ch.Kind is "MCP server" or "LSP server" ? () => x.Go("Mcp") : ch.Kind == "Hook" ? () => x.Go("Hooks") : () => { if (x.S.Items.FirstOrDefault(i => i.Path == ch.Path && i.Name == ch.Name) is { } it) { x.Open(it); } }));
            body = new StackPanel
            {
                Spacing = Space.S,
                Children = { Table.Build([new Col("", "100"), new Col("", ".9*"), new Col("Name", "1.6*"), new Col("App", "1.2*"), new Col("", "1.6*")], rows) }
            };
            if (c.Changes.Count > 12)
            {
                ((StackPanel)body).Children.Add(Txt($"… and {c.Changes.Count - 12} more", TextSize.Caption, false, Muted));
            }
        }
        return Section("arrow_sync", title, body);
    }

    /// <summary>The Health tile while the projects' checks run (after the dashboard shows): a count now would be too low.</summary>
    private static Border HealthPending(Ctx x)
    {
        // The same stack as the finished tile (number, bar, legend line), so the card keeps its size when the count arrives
        var card = Section("heart_pulse", "Health", new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                Title("0", TextSize.Display).With(t =>
                {
                    t.Opacity = 0;
                    t.HorizontalAlignment = HorizontalAlignment.Right;
                }),
                new Border { Height = Sizes.Bar }.Themed(Border.BackgroundProperty, BorderKey),
                Busy.Status("Checking projects…", out _)
            }
        });
        card.Cursor = Hand;
        card.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left) { x.Go("Health"); } };
        return card;
    }

    private static Border Tile(string icon, string title, string big, Action open, params (string label, int n, string key)[] segs)
    {
        var grid = new Grid
        {
            Height = Sizes.Bar,
            ColumnSpacing = Space.XXS
        };
        var legend = new WrapPanel
        {
            ItemSpacing = Space.XL,
            LineSpacing = Space.XS
        };
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
            Children = { Title(big, TextSize.Display).With(t =>
            {
                t.FontFeatures = _tabular;
                t.HorizontalAlignment = HorizontalAlignment.Right;
                if (long.TryParse(big, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var n))
                {
                    Motion.CountUp(t, "tile:" + title, n);   // counts up the first time, and from the old number when it changed
                }
            }),
                new Border { Child = Motion.Grow(grid, $"tile:{title}:{string.Join(",", segs.Select(s => s.n))}") }, legend }
        });
        card.Cursor = Hand;
        card.PointerReleased += (_, _) => open();
        return card;
    }

    /// <summary>Tabular figures: numbers line up.</summary>
    private static readonly FontFeatureCollection _tabular = [FontFeature.Parse("tnum")];
}
