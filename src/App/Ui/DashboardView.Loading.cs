// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class DashboardView
{
    // What the first scan is doing, in the order it does it (shown in the page's loading block)
    internal const string Finding = "Finding apps…";
    internal const string Comparing = "Comparing changes…";
    internal const string Configs = "Checking configs…";
    internal static string Safety(int done, int total) => $"Safety check {done}/{total}";

    /// <summary>
    /// The dashboard before the first scan is done: its title and buttons are the real ones and one loading block, with what the scan is doing, stands for the whole page
    /// (the cards would all say the same). It fades in; the finished dashboard (<see cref="Build"/>) replaces it and fades in too.
    /// <paramref name="status"/> sets what the block says the scan is doing ("Safety check 45/120").
    /// </summary>
    public static Control Loading(Ctx x, Action addProject, out Action<string> status)
    {
        var head = Head();
        var btns = Row(Btn("Add project folder", addProject, false, "folder_add"), Btn("Rescan", x.Rescan, true, "arrow_sync"));
        DockPanel.SetDock(btns, Dock.Right);
        head.Children.Add(btns);
        head.Children.Add(H1("Dashboard"));

        var label = Txt(Finding, TextSize.Small, false, Muted).With(l => l.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center);
        status = text => label.Text = text;
        // The loading block sits in the middle of the room under the title, both ways
        var block = new StackPanel
        {
            Spacing = Space.L,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children = { new DotsLoader(Sizes.Loader).With(d => d.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center), label }
        };
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Avalonia.Thickness(Space.Page, Space.XXXL), RowSpacing = Space.XXL };
        page.Children.Add(head);
        Grid.SetRow(block, 1);
        page.Children.Add(block);
        return Reveal.Fade(page);
    }
}
