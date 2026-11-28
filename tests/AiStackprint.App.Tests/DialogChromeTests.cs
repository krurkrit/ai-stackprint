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
        Assert.Equal(Dialogs.OuterWidth(Widths.DialogL), d.Width);
        d.Close();
    }

    [AvaloniaFact]
    public async Task Add_app_fills_in_rows_from_the_folder_and_saves_the_rows_that_are_left()
    {
        var folder = Directory.CreateTempSubdirectory("sp-addapp-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "skills"));
            File.WriteAllText(Path.Combine(folder, "mcp.json"), "{}");
            var owner = Ui.Show(new Panel());
            var task = new AddAppDialog().ShowDialog<AiStackprint.Models.AppDefinition?>(owner);
            Dispatcher.UIThread.RunJobs();
            var d = owner.OwnedWindows.Single();
            foreach (var heading in new[] { "Skills", "Config files", "Commands", "Agents", "Logs", "\"skills\"", "\"configs\"" })
            {
                Assert.Contains(heading, Ui.Texts(d));   // the same lists as apps.json
            }

            Assert.Equal("agent", d.GetVisualDescendants().OfType<ComboBox>().First().SelectedItem);   // most new tools are agents
            TextBox Box(string placeholder) => d.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == placeholder);   // a combo box has a text box of its own
            List<TextBox> Paths() => [.. d.GetVisualDescendants().OfType<TextBox>().Where(t => t.PlaceholderText?.StartsWith("e.g. ~/.myapp/", StringComparison.Ordinal) == true)];
            Box("e.g. My Agent").Text = "Probe Tool";
            Box("e.g. ~/.myapp").Text = folder;
            Ui.WaitUntil(() => Paths().Count == 2);
            Assert.Contains(Paths(), t => t.Text!.EndsWith("/skills", StringComparison.Ordinal));

            var mcp = Paths().Single(t => t.Text!.EndsWith("/mcp.json", StringComparison.Ordinal));
            Ui.Click(((Grid)mcp.Parent!).Children.OfType<Button>().Single(b => ToolTip.GetTip(b) is "Remove"));
            Dispatcher.UIThread.RunJobs();
            Ui.Click(d.GetVisualDescendants().OfType<Button>().Where(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Add")).ElementAt(3));   // Agents
            Dispatcher.UIThread.RunJobs();
            Box("e.g. ~/.myapp/agents").Text = ".probe/agents";
            d.GetVisualDescendants().OfType<ComboBox>().Last().SelectedItem = "Project";

            Ui.Click(Ui.Button(d, "Save"));
            Dispatcher.UIThread.RunJobs();
            var app = Assert.IsType<AiStackprint.Models.AppDefinition>(await task);
            Assert.Equal("custom.probe-tool", app.Id);
            Assert.Single(app.Skills);
            Assert.Empty(app.Configs);   // removed
            Assert.Equal((".probe/agents", "Project"), (app.Agents[0].Path, app.Agents[0].Level));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public void Add_app_says_what_is_missing_instead_of_closing()
    {
        var d = Open();
        Ui.Click(Ui.Button(d, "Save"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(d.IsVisible);
        Assert.Contains("Enter the app's name and its config folder.", Ui.Texts(d));
        d.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_dialog_shadow_fits_in_the_room_around_the_card_so_no_edge_cuts_it(bool dark)
    {
        foreach (var s in Theme.DialogShadow(dark))
        {
            var reach = s.Blur + s.Spread;
            Assert.True(Math.Abs(s.OffsetX) + reach <= Sizes.ShadowRoom && Math.Abs(s.OffsetY) + reach <= Sizes.ShadowRoom, $"{s}: reaches past {Sizes.ShadowRoom} px");
            Assert.True(s.Blur - Math.Abs(s.OffsetY) > 0, $"{s}: shows on every side, top too");
        }
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
