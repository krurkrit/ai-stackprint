// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public class WidgetTests
{
    [AvaloniaFact]
    public void Checks_never_uncheck_the_last_box()
    {
        var off = new HashSet<string>();
        var changes = 0;
        var root = W.Checks([(W.Txt("Project"), "Project"), (W.Txt("User"), "User")], off, () => changes++);
        Ui.Show(root);
        var boxes = root.GetVisualDescendants().OfType<CheckBox>().ToList();

        boxes[0].IsChecked = false;
        Assert.Equal(["Project"], off);
        Assert.Equal(1, changes);

        boxes[1].IsChecked = false;   // the last one checked: unchecking would hide everything
        Assert.True(boxes[1].IsChecked);
        Assert.Equal(["Project"], off);
        Assert.Equal(1, changes);

        boxes[0].IsChecked = true;
        Assert.Empty(off);
        Assert.Equal(2, changes);
    }

    [AvaloniaFact]
    public void Radios_report_a_return_to_the_first_choice()
    {
        var picks = new List<string>();
        var root = W.Radios([("All", ""), ("Agent only", "agent")], "", picks.Add);
        Ui.Show(root);
        var radios = root.GetVisualDescendants().OfType<RadioButton>().ToList();

        radios[1].IsChecked = true;
        radios[0].IsChecked = true;   // back to the value it started with: missed before, so the list stayed filtered
        Assert.Equal(["agent", ""], picks);
    }

    [AvaloniaFact]
    public void Pills_mark_the_current_choice_and_report_a_click()
    {
        string? picked = null;
        var root = W.Pills([("7 days", "7"), ("30 days", "30"), ("All", "0")], "30", v => picked = v);
        Ui.Show(root);

        Assert.Contains("accent", Ui.Button(root, "30 days").Classes);
        Assert.DoesNotContain("accent", Ui.Button(root, "All").Classes);
        Ui.Click(Ui.Button(root, "All"));
        Assert.Equal("0", picked);
    }

    [AvaloniaFact]
    public void Short_toasts_do_not_push_out_a_pinned_card()
    {
        var host = new StackPanel();
        Toast.Host = host;
        try
        {
            Ui.Show(host);
            var card = new Border();
            Toast.Pin(card);
            for (var i = 0; i < 5; i++)
            {
                Toast.Show("Path copied");
            }

            Assert.Contains(card, host.Children);
            Assert.Equal(3, host.Children.Count(c => c != card));   // at most three short toasts at a time

            Toast.Unpin(card);
            Assert.DoesNotContain(card, host.Children);
        }
        finally
        {
            Toast.Host = null;
        }
    }
}
