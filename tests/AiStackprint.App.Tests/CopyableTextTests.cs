// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class CopyableTextTests
{
    private static Point Center(Visual v, Window w) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), w)!.Value;

    [AvaloniaFact]
    public void Text_can_be_selected_and_the_selection_copied()
    {
        var text = Assert.IsType<CopyableText>(W.Txt("select this sentence please", TextSize.Body));
        var w = new Window
        {
            Width = 500,
            Height = 200,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { text }
            }
        };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(text.InClickTarget);
        text.SelectionStart = 7;
        text.SelectionEnd = 11;
        Assert.Equal("this", text.SelectedText);
        text.SelectAll();
        Assert.Equal("select this sentence please", text.SelectedText);
    }

    [AvaloniaFact]
    public void Text_inside_a_clickable_row_does_not_select_and_the_click_still_reaches_the_row()
    {
        var clicks = 0;
        var table = Table.Build([new Col("Name", "*")], [new TableRow([W.Txt("click me")], () => clicks++)]);
        var w = new Window
        {
            Width = 500,
            Height = 200,
            Content = table
        };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var text = w.GetVisualDescendants().OfType<CopyableText>().First(t => t.Text == "click me");
        Assert.True(text.InClickTarget);
        var at = Center(text, w);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Assert.Equal(1, clicks);
        Assert.True(string.IsNullOrEmpty(text.SelectedText));
        Assert.Null(text.Cursor);   // the row's hand shows, not the text cursor
    }

    [AvaloniaFact]
    public void Text_inside_a_button_does_not_select_and_the_button_still_clicks()
    {
        var clicks = 0;
        var button = new Button { Content = W.Txt("press") };
        button.Click += (_, _) => clicks++;
        var w = new Window
        {
            Width = 300,
            Height = 120,
            Content = button
        };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var text = w.GetVisualDescendants().OfType<CopyableText>().Single();
        var at = Center(text, w);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Assert.Equal(1, clicks);
        Assert.True(text.InClickTarget);
    }
}
