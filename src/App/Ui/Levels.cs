// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Levels in priority order (highest first), their explanations, and the ⓘ pop-up that shows them.</summary>
internal static class Levels
{
    public static readonly string[] Order = ["System", "Local", "Project", "User"];

    public static int Rank(string level)
    {
        var i = Array.IndexOf(Order, level);
        return i < 0 ? Order.Length : i;
    }

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
        var b = IconBtn("info", "What do the levels mean?", () => { });
        b.Flyout = InfoFlyout.Make(Help(), PlacementMode.BottomEdgeAlignedLeft);
        return b;
    }

    private static readonly Dictionary<string, string> _short = new()
    {
        ["System"] = "Set by an administrator for the whole machine.",
        ["Local"] = "In the project, not committed (git-ignored).",
        ["Project"] = "In the repository, shared with the team.",
        ["User"] = "In your home folder, for every project."
    };

    /// <summary>The levels explanation (the content of the ⓘ pop-up, also on the Level column header): one line each.</summary>
    public static Control Help()
    {
        var list = HeaderHelp.Panel("Levels, highest priority first", Order.Select(l => (l, _short[l])));
        list.Children.Add(Txt("A higher level wins for the same key.", TextSize.Caption, false, Text));
        return list;
    }
}
