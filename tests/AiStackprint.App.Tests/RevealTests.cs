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
    public void The_dashboard_before_the_first_scan_has_its_title_and_one_loading_block_for_the_page()
    {
        var w = new Window { Width = 1200, Height = 900 };
        w.Content = DashboardView.Loading(out var status);
        w.Show();
        var t = Ui.Texts(w);
        Assert.Contains("Dashboard", t);
        Assert.DoesNotContain("Rescan", t);   // the button is in the title bar
        Assert.Equal(1, t.Count(s => s == DashboardView.Finding));   // one message for the whole page, not one per card
        Assert.DoesNotContain("Skills by app", t);

        status(DashboardView.Safety(45, 120));   // the one block says what the scan is doing
        Assert.Equal(1, Ui.Texts(w).Count(s => s == "Safety check 45/120"));
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
