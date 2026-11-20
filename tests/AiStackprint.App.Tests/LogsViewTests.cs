// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class LogsViewTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("ailogs-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    /// <summary>A Logs page for one app whose folder holds <paramref name="count"/> log files, newest first: log00.log … </summary>
    private (Window W, LogsView View) Page(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var f = Path.Combine(_tmp, $"log{i:00}.log");
            File.WriteAllText(f, $"line of log {i}\n");
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(-i));
        }

        var def = new AppDefinition
        {
            Id = "app",
            Name = "Test app",
            Category = "cli",
            Logs = [new LogSource
            {
                Path = _tmp,
                Pattern = "*.log"
            }]
        };
        var s = new AppState
        {
            Defs = [def],
            Apps = [new AppInfo("app", "Test app", "cli", null, true, null)]
        };
        var w = new Window
        {
            Width = 1200,
            Height = 700
        };
        var x = new Ctx(s, w, _ => { }, () => { });
        var view = new LogsView(x);
        w.Content = view.Build("app");
        w.Show();
        Ui.WaitUntil(() => Ui.Texts(w).Contains("log00.log") && !Ui.Texts(w).Contains("Reading log folders…"));
        return (w, view);
    }

    [AvaloniaFact]
    public void The_files_list_shows_the_newest_50_and_a_button_for_older_ones()
    {
        var (w, _) = Page(60);
        Assert.Contains("log49.log", Ui.Texts(w));
        Assert.DoesNotContain("log50.log", Ui.Texts(w));

        var more = w.GetVisualDescendants().OfType<Button>().First(b => Ui.Texts(b).Any(t => t.StartsWith("Show 10 more", StringComparison.Ordinal)));
        Ui.Click(more);
        Ui.WaitUntil(() => Ui.Texts(w).Contains("log59.log"));
    }

    [AvaloniaFact]
    public void Clicking_a_file_opens_it_without_scrolling_the_page_back_to_the_top()
    {
        var (w, _) = Page(8);
        // the summary and the open file arrive later and would move the row under the click
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.StartsWith("Largest: ", StringComparison.Ordinal)) && !w.GetVisualDescendants().Any(v => v.Classes.Contains(Busy.Class) && v.IsEffectivelyVisible));
        w.UpdateLayout();
        var scroll = w.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.Extent.Height > v.Viewport.Height).OrderByDescending(v => v.Extent.Height).First();
        var name = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "log03.log");
        scroll.Offset = new Vector(0, Math.Min(name.TranslatePoint(default, scroll.Content as Visual ?? scroll)!.Value.Y - 50, scroll.Extent.Height - scroll.Viewport.Height));
        Ui.WaitUntil(() => scroll.Offset.Y > 0);
        w.UpdateLayout();
        var before = scroll.Offset;

        bool Opened() => Ui.Texts(w).Count(t => t == "log03.log") == 2;   // in the list and as the viewer's title
        for (var attempt = 0; attempt < 5 && !Opened(); attempt++)
        {
            // A click is hit-tested against the last rendered frame, and the folder watcher may have rebuilt the list: render, then find the row again
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var cell = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "log03.log");
            var at = cell.TranslatePoint(new Point(2, 2), w)!.Value;
            w.MouseDown(at, MouseButton.Left);
            w.MouseUp(at, MouseButton.Left);
            for (var i = 0; i < 25 && !Opened(); i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }
        }

        Assert.True(Opened());
        Assert.Equal(before, scroll.Offset);
    }
}
