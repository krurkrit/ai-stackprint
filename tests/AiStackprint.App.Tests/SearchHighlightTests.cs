// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;

using Xunit;

namespace AiStackprint.App.Tests;

public class SearchHighlightTests
{
    [AvaloniaFact]
    public void Search_words_are_highlighted_in_name_and_description()
    {
        var view = (StackPanel)Table.NameDesc("pdf-tools", "Merge PDF files", marks: SearchQuery.Parse("pdf author:jane"));
        Ui.Show(view);
        var name = (TextBlock)view.Children[0];
        var desc = (TextBlock)view.Children[1];

        Assert.Equal(["pdf", "-tools"], name.Inlines!.OfType<Run>().Select(r => r.Text));
        Assert.Equal(["Merge ", "PDF", " files"], desc.Inlines!.OfType<Run>().Select(r => r.Text));
        Assert.NotNull(name.Inlines!.OfType<Run>().First().Background);   // the match
        Assert.Null(name.Inlines!.OfType<Run>().Last().Background);
    }

    [AvaloniaFact]
    public void Text_without_a_match_stays_plain()
    {
        var view = (StackPanel)Table.NameDesc("deploy", "Ship it", marks: SearchQuery.Parse("author:jane"));
        Assert.Equal("deploy", ((TextBlock)view.Children[0]).Text);
        Assert.Empty(((TextBlock)view.Children[0]).Inlines ?? []);
    }
}
