// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class RevealTests
{
    [AvaloniaFact]
    public void Busy_Into_shows_the_loading_icon_first_then_the_result_which_fades_in()
    {
        var w = new Window { Width = 800, Height = 600 };
        var host = new ContentControl();
        w.Content = host;
        w.Show();
        using var gate = new ManualResetEventSlim();
        Busy.Into(host, "Loading it…", _ => { gate.Wait(TestContext.Current.CancellationToken); return "done"; }, r => new TextBlock { Text = r });
        Assert.Contains("Loading it…", Ui.Texts(w));
        gate.Set();
        Ui.WaitUntil(() => Ui.Texts(w).Contains("done"));
        Assert.NotEmpty(((Control)host.Content!).Transitions!);   // the result carries the fade
    }

    [AvaloniaFact]
    public void Fade_starts_transparent()
    {
        Assert.Equal(0, Reveal.Fade(new Border()).Opacity);
    }

    [AvaloniaFact]
    public void The_dashboard_before_the_first_scan_has_its_buttons_and_cards_with_loading_icons()
    {
        var w = new Window { Width = 1200, Height = 900 };
        var x = new Ctx(new AiStackprint.Models.AppState(), w, _ => { }, () => { });
        w.Content = DashboardView.Loading(x, () => { });
        w.Show();
        var t = Ui.Texts(w);
        Assert.Contains("Dashboard", t);
        Assert.Contains("Rescan", t);
        Assert.Contains("Skills by app", t);
    }
}
