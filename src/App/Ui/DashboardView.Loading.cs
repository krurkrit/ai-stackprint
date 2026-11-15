// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class DashboardView
{
    // What the first scan is doing, in the order it does it (shown in the loading cards)
    internal const string Finding = "Finding apps, skills and plugins…";
    internal const string Comparing = "Comparing with the last scan…";
    internal const string Configs = "Checking config files and projects…";
    internal static string Safety(int done, int total) => $"Checking skills and plugins for safety: {done} of {total}";

    /// <summary>
    /// The dashboard before the first scan is done: its title, buttons and cards are the real ones, each card holds the loading icon where its data will be.
    /// It fades in; the finished dashboard (<see cref="Build"/>) replaces it and fades in too.
    /// <paramref name="status"/> sets what every card says the scan is doing ("Checking skills for safety: 45 of 120").
    /// </summary>
    public static Control Loading(Ctx x, Action addProject, out Action<string> status)
    {
        var head = new DockPanel();
        var btns = Row(Btn("Add project folder", addProject, false, "folder_add"), Btn("Rescan", x.Rescan, true, "arrow_sync"));
        DockPanel.SetDock(btns, Dock.Right);
        head.Children.Add(btns);
        head.Children.Add(H1("Dashboard"));

        var labels = new List<TextBlock>();
        status = text => labels.ForEach(l => l.Text = text);
        Control Card(string icon, string title)
        {
            var c = Section(icon, title, Busy.Status(Finding, out var l));
            labels.Add(l);
            return c;
        }
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = Space.XL };
        var titles = new[] { ("apps", "Apps installed"), ("puzzle_piece", "Plugins"), ("sparkle", "Skills"), ("warning", "Health warnings") };
        for (var i = 0; i < titles.Length; i++)
        {
            var c = Card(titles[i].Item1, titles[i].Item2);
            Grid.SetColumn(c, i);
            tiles.Children.Add(c);
        }

        var charts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = Space.XL };
        var left = new StackPanel { Spacing = Space.XL, Children = { Card("sparkle", "Skills by app"), Card("puzzle_piece", "Plugins by app"), Card("wrench_screwdriver", "What plugins add") } };
        var right = new StackPanel { Spacing = Space.XL, Children = { Card("people", "Skills and plugins by author") } };
        Grid.SetColumn(right, 1);
        charts.Children.Add(left); charts.Children.Add(right);
        return Reveal.Fade(Page(head, tiles, charts));
    }
}
