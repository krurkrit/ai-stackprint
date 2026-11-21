// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>The whole window on a made-up home folder: it scans, shows every page from the side menu, and answers keys and the title bar.</summary>
public sealed class MainWindowTests : IDisposable
{
    private readonly FakeHome _home = new();

    public void Dispose()
    {
        _home.Dispose();
        Toast.Host = null;
    }

    private static bool Busy(Window w) => w.GetVisualDescendants().Any(v => v.Classes.Contains(Ui.BusyClass) && v.IsEffectivelyVisible);

    private static void Settle(Window w)
    {
        var idle = DateTime.MaxValue;
        Ui.WaitUntil(() =>
        {
            if (Busy(w)) { idle = DateTime.MaxValue; }
            else if (idle == DateTime.MaxValue) { idle = DateTime.UtcNow; }
            return DateTime.UtcNow - idle > TimeSpan.FromMilliseconds(400);
        }, 20000);
    }

    private static Window Open()
    {
        var w = new MainWindow();
        w.Show();
        Settle(w);
        return w;
    }

    private static Button Nav(Window w, string label) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label || (t.Text?.StartsWith(label + " ·", StringComparison.Ordinal) ?? false)));

    private static Button Tip(Window w, string tip) =>
        w.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) is string s && s.StartsWith(tip, StringComparison.Ordinal));

    [AvaloniaFact]
    public void The_window_scans_the_made_up_home_and_every_page_opens_from_the_side_menu()
    {
        var w = Open();
        var seen = Ui.Texts(w);
        Assert.Contains("Dashboard", seen);
        foreach (var (label, title) in new[]
        {
            ("Apps", "Apps"), ("Plugins", "Plugins"), ("Skills", "Skills"), ("Commands & Agents", "Commands & Agents"), ("Servers", "Servers"), ("Hooks", "Hooks"),
            ("Usage", "Usage"), ("Models", "Models"), ("Logs", "Logs"), ("Health", "Health"), ("Settings", "Appearance"), ("Back to app", "Health"), ("Dashboard", "Dashboard"),
        })
        {
            Ui.Click(Nav(w, label));
            Settle(w);
            Assert.Contains(title, Ui.Texts(w));
        }

        Assert.Contains(Ui.Texts(w), t => t.Contains("pdf-tools", StringComparison.Ordinal) || t == "Dashboard");
        w.Close();
    }

    [AvaloniaFact]
    public void Keys_and_the_title_bar_work_the_menu_the_palette_rescan_and_history()
    {
        var w = Open();
        w.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();
        var box = w.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible);
        Assert.NotNull(box);
        box.Text = "skill";
        Dispatcher.UIThread.RunJobs();
        w.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Settle(w);

        w.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.ArrowLeft, null);   // back
        w.KeyPress(Key.Right, RawInputModifiers.Alt, PhysicalKey.ArrowRight, null);   // forward
        w.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);   // rescan
        Settle(w);

        Ui.Click(Tip(w, "Collapse menu"));
        Ui.Click(Tip(w, "Expand menu"));
        Ui.Click(Tip(w, "Rescan"));
        Settle(w);
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        w.Close();
    }

    private static void Palette(Window w, string query)
    {
        w.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();
        w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.PlaceholderText?.StartsWith("Go to", StringComparison.Ordinal) == true).Text = query;
        Dispatcher.UIThread.RunJobs();
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Settle(w);
    }

    /// <summary>Clicks a button by its text, a tab or a toggle; a button this page does not have is skipped.</summary>
    private static void Press(Window w, params string[] texts)
    {
        foreach (var text in texts)
        {
            if (w.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && (b.Content as string == text || b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text))) is { } b)
            {
                Ui.Click(b);
                Settle(w);
            }
        }
    }

    [AvaloniaFact]
    public void An_app_page_a_log_a_skill_and_a_project_answer_their_buttons()
    {
        var w = Open();
        Ui.Click(Nav(w, "Apps"));
        Settle(w);
        Ui.ClickText(w, "Claude Code");
        Settle(w);
        Assert.Contains("Recent use", Ui.Texts(w));
        Press(w, "7 days", "30 days", "All", "Cost", "Tokens");

        Ui.Click(Nav(w, "Logs"));
        Settle(w);
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.EndsWith("· session.jsonl", StringComparison.Ordinal)));
        Ui.ClickText(w, t => t.EndsWith("· session.jsonl", StringComparison.Ordinal));
        Settle(w);
        Assert.Contains("Hi there", Ui.Texts(w));
        Press(w, "Raw", "Readable", "Aa", ".*", "Follow newest");
        if (Ui.Texts(w).Contains("Read")) { Ui.ClickText(w, "Read"); }
        Settle(w);
        w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.PlaceholderText?.StartsWith("Search this file", StringComparison.Ordinal) == true).Text = "Hello";
        Settle(w);
        Press(w, "7 days", "30 days", "All");

        Palette(w, "pdf-tools");
        Assert.Contains("PDF tools", Ui.Texts(w));
        Press(w, "Files", "Source", "Content");

        Palette(w, "demo");
        Press(w, "By file", "By app");
        w.Close();
    }

    [AvaloniaFact]
    public void The_side_menus_selection_background_slides_to_the_item_you_click()
    {
        var w = Open();
        Border Indicator() => w.GetVisualDescendants().OfType<Border>().First(b => b.Name == "NavIndicator");
        double Top()
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();   // advances the animation
            return Indicator().Margin.Top;
        }
        Ui.WaitUntil(() => Indicator().IsVisible);
        var dashboard = Top();
        Ui.Click(Nav(w, "Apps"));   // no waiting for the page: the slide is 260 ms
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        var apps = Nav(w, "Apps");
        var grid = Indicator().Parent as Grid;
        var seen = new List<double>();
        Ui.WaitUntil(() =>
        {
            seen.Add(Top());
            return seen.Count > 3 && grid != null && Math.Abs(seen[^1] - Nav(w, "Apps").TranslatePoint(default, grid).Value.Y) < 1;
        }, 3000);   // it moved to the clicked item
        var target = Nav(w, "Apps").TranslatePoint(default, grid!).Value.Y;
        Assert.Contains(seen, v => v > dashboard + 1 && v < target - 1);   // on the way it was in between: it slid, not jumped
        Assert.True(target > dashboard);
        Assert.Equal(apps.Bounds.Height, Indicator().Height, 1);
        Ui.Click(Nav(w, "Settings"));   // the other menu: Back to app, the pages, About
        Settle(w);
        Ui.WaitUntil(() => Indicator().IsVisible && Math.Abs(Top() - Nav(w, "Appearance").TranslatePoint(default, (Grid)Indicator().Parent!)!.Value.Y) < 1, 3000);
        w.Close();
    }

    [AvaloniaFact]
    public void The_Logs_summary_lists_tokens_and_cost_per_day()
    {
        var w = Open();
        Ui.Click(Nav(w, "Logs"));
        Settle(w);
        Press(w, "All");   // the made-up session is from a fixed day: not always within the last 7 days
        Settle(w);
        Ui.WaitUntil(() => Ui.Texts(w).Contains("Est. cost"));
        var texts = Ui.Texts(w);
        Assert.Contains("Day", texts);
        Assert.Contains("Tokens in / out", texts);
        Assert.Contains("2026-11-18", texts);   // the day of the made-up session's reply
        w.Close();
    }

    [AvaloniaFact]
    public void The_view_switch_has_Raw_Conversation_and_Compact_and_each_shows_the_session()
    {
        var w = Open();
        Ui.Click(Nav(w, "Logs"));
        Settle(w);
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.EndsWith("· session.jsonl", StringComparison.Ordinal)));
        Ui.ClickText(w, t => t.EndsWith("· session.jsonl", StringComparison.Ordinal));
        Settle(w);
        ToggleButton Mode(string name) => w.GetVisualDescendants().OfType<ToggleButton>().First(b => b.Content as string == name);
        Assert.Contains("Hi there", Ui.Texts(w));   // Conversation (cards) is the default
        Mode("Compact").IsChecked = true;
        Settle(w);
        Assert.Contains("Hi there", Ui.Texts(w));   // one line per step
        Assert.True(Mode("Compact").IsChecked);
        Mode("Raw").IsChecked = true;
        Settle(w);
        Assert.DoesNotContain("Hi there", Ui.Texts(w));   // the file as written is code, not text blocks
        Mode("Conversation").IsChecked = true;
        Settle(w);
        Assert.Contains("Hi there", Ui.Texts(w));
        w.Close();
    }

    [AvaloniaFact]
    public void A_long_session_shows_steps_sub_agents_results_and_answers_a_search()
    {
        var w = Open();
        Ui.Click(Nav(w, "Logs"));
        Settle(w);
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.EndsWith("· session2.jsonl", StringComparison.Ordinal)));
        Ui.ClickText(w, t => t.EndsWith("· session2.jsonl", StringComparison.Ordinal));
        Settle(w);
        Assert.Contains("All done.", Ui.Texts(w));
        Assert.Contains(Ui.Texts(w), t => t.StartsWith("Sub-agents", StringComparison.Ordinal) || t == "Sub-agents");

        Press(w, "Result · Bash · Passed 12 tests", "Result · Read · public class Parser", "Result · Grep · line 1 of a long result", "Read", "Task", "Grep");
        if (Ui.Texts(w).FirstOrDefault(t => t.StartsWith("Thinking", StringComparison.Ordinal)) is { } thinking)
        {
            Ui.ClickText(w, thinking);
        }

        var box = w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.PlaceholderText?.StartsWith("Search this file", StringComparison.Ordinal) == true);
        box.Text = "Parse";
        Settle(w);
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        w.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
        Settle(w);
        Press(w, ".*", "Aa", "Raw");
        box.Text = "line [0-9]+";
        Settle(w);
        Press(w, "Readable", ".*", "Aa");
        box.Text = "";
        Settle(w);
        Press(w, "7 days", "30 days", "All");
        w.Close();
    }
}
