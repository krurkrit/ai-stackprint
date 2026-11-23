// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The ⓘ button after the search box of the skill, plugin and command pages: click opens how the search works (<see cref="Services.SearchQuery"/>, <see cref="Services.ItemSearch"/>).</summary>
internal static class SearchHelp
{
    private static readonly (string Field, string What)[] _fields =
    [
        ("name:", "the name"), ("desc:", "the description"), ("author:", "the author"), ("keyword: or tag:", "the keywords"), ("path:", "the file path"),
        ("has:", "what a plugin adds: skills, commands, agents, hooks, mcp, lsp")
    ];

    private static readonly (string Example, string Means)[] _examples =
    [
        ("git review", "both words, in any of name, keywords, author, description or path"),
        ("\"code review\"", "the words together, as a phrase"),
        ("author:lauren", "an author that contains lauren"),
        ("author:\"Lauren\"", "exactly Lauren, not Lauren Smith"),
        ("has:mcp keyword:git", "plugins that add MCP servers and have the keyword git")
    ];

    public static Button Button()
    {
        var list = new StackPanel
        {
            Spacing = Space.L,
            Width = Widths.DialogS
        };
        list.Children.Add(Title("Search", TextSize.Subtitle));
        list.Children.Add(Txt("Every word you type must match. Plain words look in the name, keywords, author, description and path; the best matches come first (name, then keywords, author, description, path). Put a word before a colon to search one field only.", TextSize.Small));
        list.Children.Add(Rows("One field", _fields.Select(f => (f.Field, f.What))));
        list.Children.Add(Rows("Examples", _examples.Select(e => (e.Example, e.Means))));
        list.Children.Add(Txt("Quotes after a field match its whole value. The app and the level are chosen in their menus, not in the search.", TextSize.Caption, false, Muted));
        var b = IconBtn("info", "How the search works", () => { });
        b.Flyout = new Flyout
        {
            Content = list,
            Placement = PlacementMode.BottomEdgeAlignedRight
        };
        return b;
    }

    private static StackPanel Rows(string title, IEnumerable<(string Code, string Text)> rows)
    {
        var sp = new StackPanel { Spacing = Space.XS };
        sp.Children.Add(Title(title, TextSize.Body));
        foreach (var (code, text) in rows)
        {
            sp.Children.Add(new StackPanel
            {
                Spacing = Space.XXS,
                Children = { Mono(code, TextSize.Small), Txt(text, TextSize.Small, false, Muted) }
            });
        }

        return sp;
    }
}
