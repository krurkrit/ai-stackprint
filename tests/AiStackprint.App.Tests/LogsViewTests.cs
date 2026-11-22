// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private (Window W, LogsView View) Page(int count) => Page(count, null);

    private (Window W, LogsView View) Page(int count, string? format)
    {
        for (var i = 0; i < count; i++)
        {
            var f = Path.Combine(_tmp, $"log{i:00}.log");
            File.WriteAllText(f, format == null ? $"line of log {i}\n" : $"2026-08-07 10:00:0{i} [info] started {i}\n2026-08-07 10:00:1{i} [error] failed {i}\n  at trace {i}\n2026-08-07 10:00:2{i} [error] again {i}\n");
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
                Pattern = "*.log",
                Format = format
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
        Ui.WaitUntil(() => Named(w, "log00.log") > 0 && !Ui.Texts(w).Contains("Reading log folders…"));
        return (w, view);
    }

    /// <summary>How many texts show this file: a row ("Logs · name.log": its name comes after the type) and the viewer's title (the name alone).</summary>
    private static int Named(Window w, string name) => Ui.Texts(w).Count(t => t == name || t.EndsWith("· " + name, StringComparison.Ordinal));

    [AvaloniaFact]
    public void The_files_list_shows_the_newest_50_and_a_button_for_older_ones()
    {
        var (w, _) = Page(60);
        Assert.True(Named(w, "log49.log") > 0);
        Assert.Equal(0, Named(w, "log50.log"));

        var more = w.GetVisualDescendants().OfType<Button>().First(b => Ui.Texts(b).Any(t => t.StartsWith("Show 10 more", StringComparison.Ordinal)));
        Ui.Click(more);
        Ui.WaitUntil(() => Named(w, "log59.log") > 0);
    }

    [AvaloniaFact]
    public void Clicking_a_file_opens_it_without_scrolling_the_page_back_to_the_top()
    {
        var (w, _) = Page(8);
        // the summary and the open file arrive later and would move the row under the click
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.StartsWith("Largest: ", StringComparison.Ordinal)) && !w.GetVisualDescendants().Any(v => v.Classes.Contains(Busy.Class) && v.IsEffectivelyVisible));
        w.UpdateLayout();
        var scroll = w.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.Extent.Height > v.Viewport.Height).OrderByDescending(v => v.Extent.Height).First();
        var name = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.EndsWith("· log03.log", StringComparison.Ordinal) == true);
        scroll.Offset = new Vector(0, Math.Min(name.TranslatePoint(default, scroll.Content as Visual ?? scroll)!.Value.Y - 50, scroll.Extent.Height - scroll.Viewport.Height));
        Ui.WaitUntil(() => scroll.Offset.Y > 0);
        w.UpdateLayout();
        var before = scroll.Offset;

        bool Opened() => Named(w, "log03.log") == 2;   // in the list and as the viewer's title
        for (var attempt = 0; attempt < 5 && !Opened(); attempt++)
        {
            // A click is hit-tested against the last rendered frame, and the folder watcher may have rebuilt the list: render, then find the row again
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var cell = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.EndsWith("· log03.log", StringComparison.Ordinal) == true);
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

    [AvaloniaFact]
    public void A_search_looks_in_every_log_file_and_a_result_opens_its_file()
    {
        var (w, _) = Page(5);
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.StartsWith("Largest: ", StringComparison.Ordinal)));
        w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.PlaceholderText?.StartsWith("Search this file", StringComparison.Ordinal) == true).Text = "\"of log 3\"";
        w.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Content as string == "Aa").IsChecked = true;   // searches at once (typing waits a moment)
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.StartsWith("1 match in 1 file", StringComparison.Ordinal) || t.Contains("no matches", StringComparison.Ordinal)));
        Assert.Equal(0, Named(w, "log04.log") - 1);   // only the list row: the other files are not open
        w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.StartsWith("Line 1", StringComparison.Ordinal) == true).BringIntoView();   // the results are below the first screen
        w.UpdateLayout();
        for (var attempt = 0; attempt < 5 && Named(w, "log03.log") != 2; attempt++)
        {
            Ui.ClickText(w, t => t.StartsWith("Line 1", StringComparison.Ordinal));   // a click is tested against the last drawn frame: try again if the page was redrawn meanwhile
            for (var i = 0; i < 25 && Named(w, "log03.log") != 2; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }
        }

        Assert.Equal(2, Named(w, "log03.log"));   // the file with the match is now the open one
        Assert.Contains(w.GetVisualDescendants().OfType<Control>(), c => c.Classes.Contains(Table.SelectedClass));   // its row in the Files list is the selected one (the page scrolls to it)
        Assert.Contains(Ui.Texts(w), x => x.StartsWith("1 match in 1 file", StringComparison.Ordinal));
        var query = w.GetVisualDescendants().OfType<TextBox>().First(x => x.IsEffectivelyVisible && x.PlaceholderText?.StartsWith("Search this file", StringComparison.Ordinal) == true);
        Ui.Click(w.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) is string tip && tip.StartsWith("Close the results", StringComparison.Ordinal)));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Ui.Texts(w), x => x.StartsWith("1 match in 1 file", StringComparison.Ordinal));   // the results are closed
        Assert.Equal("\"of log 3\"", query.Text);   // the search words stay
    }

    [AvaloniaFact]
    public void A_text_log_shows_one_row_per_line_with_its_level_and_a_Lines_view()
    {
        var (w, view) = Page(2, "text-log");
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.StartsWith("failed 0", StringComparison.Ordinal)));
        var seen = Ui.Texts(w);
        Assert.Contains("ERROR", seen);
        Assert.Contains("INFO", seen);
        Assert.Contains("3 lines", seen);
        Assert.Contains("Error 2", seen);   // the level chip with its count
        Assert.Contains(seen, t => t.Contains("at trace 0", StringComparison.Ordinal));   // the line under the message belongs to it
        Assert.Contains("Raw", Ui.ButtonTexts(w));   // the switch back to the file as written
        var popup = view.MarksList();   // the Bookmarks button's list: the errors first
        var listed = Ui.Texts(popup);
        Assert.Contains("Errors (2)", listed);
        Assert.Equal(2, listed.Count(t => t.StartsWith("Line ", StringComparison.Ordinal)));
    }
}
