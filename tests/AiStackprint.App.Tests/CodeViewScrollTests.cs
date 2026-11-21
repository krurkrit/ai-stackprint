// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>The raw log view: the last line scrolls up to the middle of the view, not to its bottom edge.</summary>
public sealed class CodeViewScrollTests
{
    [AvaloniaFact]
    public void The_last_line_is_in_the_middle_of_the_view()
    {
        var view = new CodeView("a.log") { ScrollPastEnd = true, Height = 400 };
        view.Text = string.Join('\n', Enumerable.Range(1, 300).Select(i => $"line {i}")) + "\n";
        var w = Ui.Show(view);
        Dispatcher.UIThread.RunJobs();
        view.ScrollToEnd();
        Dispatcher.UIThread.RunJobs();
        var editor = view.GetVisualDescendants().OfType<TextEditor>().First();
        var text = editor.TextArea.TextView;
        var line = text.GetVisualLine(300);
        Assert.NotNull(line);
        var y = line.VisualTop - text.ScrollOffset.Y;
        Assert.InRange(y, text.Bounds.Height * .25, text.Bounds.Height * .75);
        w.Close();
    }

    [AvaloniaFact]
    public void The_line_in_the_middle_of_the_view_is_known()
    {
        var view = new CodeView("a.log") { ScrollPastEnd = true, Height = 400 };
        view.Text = string.Join('\n', Enumerable.Range(1, 300).Select(i => $"line {i}")) + "\n";
        var w = Ui.Show(view);
        Dispatcher.UIThread.RunJobs();
        view.ScrollToEnd();
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(view.CenterLine(), 295, 301);   // the last line is in the middle
        view.Reveal(120, 0, 0);
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(view.CenterLine(), 115, 125);
        w.Close();
    }
}
