// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class PageHeadTests
{
    private static double TitleY(Control right)
    {
        var head = W.Head();
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }

        head.Children.Add(W.H1("Title"));
        var w = new Window
        {
            Width = 800,
            Height = 300,
            Content = W.Page(head)
        };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Title").TranslatePoint(default, w)!.Value.Y;
    }

    [AvaloniaFact]
    public void The_page_title_sits_at_the_same_height_with_a_button_with_pills_or_with_nothing_at_the_right()
    {
        var withButton = TitleY(W.Btn("Add", () => { }, true, "add"));
        var withPills = TitleY(W.Pills([("All 3", ""), ("Some 1", "s")], "", _ => { }));
        var withNothing = TitleY(new Panel());
        Assert.InRange(withPills - withButton, -1, 1);
        Assert.InRange(withNothing - withButton, -1, 1);
    }

    [AvaloniaFact]
    public void The_usage_chart_gives_each_app_its_own_color()
    {
        var keys = typeof(UsageView).GetField("_keys", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null) as string[];
        Assert.NotNull(keys);
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.DoesNotContain(W.Accent, keys);   // the accent is the first series already: ChartIn
    }
}
