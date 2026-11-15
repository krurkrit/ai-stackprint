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

    [AvaloniaFact]
    public void A_control_faded_after_it_is_already_in_the_window_still_becomes_visible()
    {
        var w = new Window { Width = 400, Height = 300 };
        var host = new ContentControl { Content = new Border() };
        w.Content = host;
        w.Show();
        var b = (Border)host.Content;
        Reveal.Fade(b);
        Ui.WaitUntil(() => { Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); return b.Opacity > 0; });   // the fade runs on the render clock
        Assert.True(b.Opacity > 0);
    }

    [AvaloniaFact]
    public void A_file_opens_in_the_background_and_shows_its_text()
    {
        var file = Path.Combine(Path.GetTempPath(), "reveal-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(file, "hello from the file");
        try
        {
            var w = new Window { Width = 800, Height = 600, Content = FileView.Open(file, false, _ => { }) };
            w.Show();
            Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.Contains("hello from the file")));
        }
        finally { File.Delete(file); }
    }
}
