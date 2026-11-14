// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Levels in priority order (highest first), their explanations, and the ⓘ pop-up that shows them.</summary>
internal static class Levels
{
    public static readonly string[] Order = ["System", "Local", "Project", "User"];

    public static int Rank(string level) { var i = Array.IndexOf(Order, level); return i < 0 ? Order.Length : i; }

    public static readonly Dictionary<string, (string Where, string Who, string Note)> Info = new()
    {
        ["System"] = ("Machine-wide folders set by an administrator (e.g. Program Files, /etc)", "Everyone on this computer", "Highest. Policy: nothing below can override it. Usually empty on personal machines."),
        ["Local"] = ("Inside the project, not committed (git-ignored), e.g. CLAUDE.local.md", "Only you, only this project", "Overrides Project and User. For personal tweaks to one repo."),
        ["Project"] = ("Inside the repository, committed with the code", "Everyone who works in this repo", "Overrides User. Team rules for one codebase."),
        ["User"] = ("Your home folder, e.g. ~/.claude", "You, in every project", "Lowest. Your personal defaults everywhere."),
    };

    public static string Describe(string level) =>
        Info.TryGetValue(level, out var i) ? $"{level}: {i.Where}\nApplies to: {i.Who}\n{i.Note}" : level;

    /// <summary>Small ⓘ button; click opens the levels explanation.</summary>
    public static Button InfoButton()
    {
        var list = new StackPanel { Spacing = Space.L, Width = Widths.DialogS };
        list.Children.Add(Title("Levels, highest priority first", TextSize.Subtitle));
        foreach (var l in Order)
        {
            var (where, who, note) = Info[l];
            var head = WithDot(LevelDot(l), l, TextSize.Body, true);
            list.Children.Add(new StackPanel { Spacing = Space.XXS, Children = { head, Txt(where, TextSize.Small), Txt("Applies to: " + who, TextSize.Small, false, Muted), Txt(note, TextSize.Small, false, Muted) } });
        }
        list.Children.Add(Txt("Settings follow this order: a higher level wins for the same key. Instruction files and skills are combined; when they conflict, the more specific one usually wins. Apps use their own words for levels (ADMIN, REPO, Workspace); hover a level to see it.",
            TextSize.Caption, false, Muted));
        var b = IconBtn("info", "What do the levels mean?", () => { });
        b.Flyout = new Flyout { Content = list, Placement = PlacementMode.BottomEdgeAlignedLeft };
        return b;
    }
}
