// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class AppsViewTests
{
    private static (Window W, AppsView View) Open(double width)
    {
        var s = new AppState
        {
            Apps =
            [
                new AppInfo("a", "Alpha", "agent", null, true, null), new AppInfo("b", "Beta", "agent", null, true, null),
                new AppInfo("c", "Gamma", "agent", null, false, null), new AppInfo("d", "Delta", "editor", null, true, null),
            ]
        };
        s.Settings.HideNotInstalled = false;
        var w = new Window
        {
            Width = width,
            Height = 800
        };
        AppsView? view = null;
        var x = new Ctx(s, w, _ => { }, () => { }) { Refresh = () => w.Content = view!.Build() };
        view = new AppsView(x);
        w.Content = view.Build();
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return (w, view);
    }

    [AvaloniaFact]
    public void A_group_says_how_many_apps_it_holds_not_how_many_the_tool_knows()
    {
        var (w, _) = Open(1400);
        var texts = Ui.Texts(w);
        Assert.Contains("2", texts);   // the agents installed here
        Assert.DoesNotContain(texts, t => t.Contains(" of ", StringComparison.Ordinal) && t.Contains("installed", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_count_columns_are_equally_wide_and_the_status_column_is_narrower_than_a_type_column()
    {
        var cols = new[] { Cols.Name("App"), Cols.Type("Category"), Cols.Status(), Cols.Count("Plugins"), Cols.Count("Skills"), Cols.Count("Commands"), Cols.Count("Config files") };
        var counts = cols.Where(c => c.Right).Select(c => c.Width).Distinct().ToList();
        Assert.Single(counts);
        Assert.True(double.Parse(Cols.Status().Width, System.Globalization.CultureInfo.InvariantCulture) < double.Parse(Cols.Type().Width, System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void In_a_narrow_window_the_table_scrolls_sideways_and_the_last_column_can_be_reached()
    {
        var (w, _) = Open(700);
        var sv = w.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto);
        Assert.True(sv.Extent.Width > sv.Viewport.Width, $"extent {sv.Extent.Width} viewport {sv.Viewport.Width}");
        sv.Offset = new Avalonia.Vector(sv.Extent.Width, 0);
        Dispatcher.UIThread.RunJobs();
        Assert.True(sv.Offset.X > 0);
        Assert.Contains("Config files", Ui.Texts(w));

        var (wide, _) = Open(1800);
        var wideSv = wide.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto);
        Assert.True(wideSv.Extent.Width <= wideSv.Viewport.Width + 1);   // wide enough: nothing to scroll
    }
}
