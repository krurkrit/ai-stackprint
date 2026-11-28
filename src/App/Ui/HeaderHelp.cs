// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The explanations behind the ⓘ on table column headers (<see cref="Col.Help"/>); the levels one is <see cref="Levels.Help"/>. Short: a name and one line each.</summary>
internal static class HeaderHelp
{
    /// <summary>A name with one line under it per item, under <paramref name="title"/> (the pop-up's title).</summary>
    internal static InfoPanel Panel(string title, IEnumerable<(string Head, string Body)> items)
    {
        var list = new InfoPanel(title)
        {
            Spacing = Space.M,
            Width = Widths.DialogS
        };
        foreach (var (head, body) in items)
        {
            list.Children.Add(new StackPanel
            {
                Children = { Txt(head, TextSize.Body, true), Txt(body, TextSize.Small, false, Text) }
            });
        }

        return list;
    }

    /// <summary>What each kind of app is (the Category column of the Apps page); the same kinds as the Add an app dialog offers.</summary>
    public static Control Categories() => Panel("Categories", AppCategories.Order.Select(c => (AppsView.Plural(c), AppCategories.About[c])));

    /// <summary>Who can start a skill (the Invoked by column of the Skills page).</summary>
    public static Control InvokedBy() => Panel("Invoked by",
    [
        ("Agent only", "The agent starts it when the task fits."),
        ("User only", "Only you start it, with a slash command."),
        ("Agent and User", "Either of you can start it.")
    ]);
}
