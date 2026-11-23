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

public sealed class DialogChromeTests
{
    private static Window Open()
    {
        var d = new AddAppDialog();
        d.Show();
        Dispatcher.UIThread.RunJobs();
        return d;
    }

    [AvaloniaFact]
    public async Task Esc_and_the_close_button_close_the_Add_app_dialog_with_no_app_instead_of_throwing()
    {
        var owner = Ui.Show(new Panel());
        foreach (var byEsc in new[] { true, false })
        {
            var task = new AddAppDialog().ShowDialog<AiStackprint.Models.AppDefinition?>(owner);
            Dispatcher.UIThread.RunJobs();
            var d = owner.OwnedWindows.Single();
            if (byEsc)
            {
                d.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            }
            else
            {
                Ui.Click(d.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) is "Close"));
            }

            Dispatcher.UIThread.RunJobs();
            Assert.Null(await task);   // a cast of false to AppDefinition used to throw and close the app
        }
    }

    [AvaloniaFact]
    public void A_dialog_is_a_floating_card_with_a_close_button_not_a_framed_window()
    {
        var d = Open();
        Assert.Equal(WindowDecorations.None, d.WindowDecorations);
        Assert.False(d.CanResize);
        var card = Assert.IsType<Border>(d.Content);
        Assert.Equal(new CornerRadius(Radius.XL), card.CornerRadius);   // Fluent UI 2: 12 px
        Assert.Equal(new Thickness(Sizes.ShadowRoom), card.Margin);     // room for the shadow
        Assert.True(card.BoxShadow.Count > 0);
        Assert.Contains("Add an app to scan", Ui.Texts(d));
        Assert.Contains(d.GetVisualDescendants().OfType<Button>(), b => ToolTip.GetTip(b) is "Close");
        Assert.Equal(Dialogs.OuterWidth(Widths.DialogM), d.Width);
        d.Close();
    }

    [AvaloniaFact]
    public void The_close_button_and_Esc_both_close_the_dialog()
    {
        var d = Open();
        Ui.Click(d.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) is "Close"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(d.IsVisible);

        var e = Open();
        e.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(e.IsVisible);
    }
}
