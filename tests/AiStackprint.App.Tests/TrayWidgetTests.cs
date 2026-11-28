// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>The tray, the usage widget, its settings, closing to the tray and alerts, with a fake lifetime, login entry and notifier (nothing touches this machine).</summary>
public sealed partial class TrayWidgetTests : IDisposable
{
    private static readonly string? _out = Environment.GetEnvironmentVariable("STACKPRINT_SHOTS");
    private readonly FakeHome _home = new();
    private readonly FakeLifetime _life = new();
    private readonly FakeLogin _login = new();
    private readonly FakeNotifier _notes = new();
    private readonly ILoginItem _realLogin = LoginItem.Current;
    private readonly INotifier _realNotifier = Notifier.Current;
    private readonly List<AppHost> _hosts = [];

    public TrayWidgetTests()
    {
        LoginItem.Current = _login;
        Notifier.Current = _notes;
    }

    public void Dispose()
    {
        foreach (var h in _hosts)
        {
            h.Quit();   // stops its timers and pipe listener, so nothing of this test runs into the next one
        }

        LoginItem.Current = _realLogin;
        Notifier.Current = _realNotifier;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        Theme.Appearance(Application.Current, SettingsStore.Defaults());
        _home.Dispose();
    }

    /// <summary>A host with live limits on for Claude Code and Codex and the readings of the mockup (Thursday 08:42 is "now" only in the texts' shape).</summary>
    private AppHost Host(bool tray = true, Action<Models.Settings>? set = null)
    {
        var h = new AppHost(_life, tray ? ["--tray"] : []) { Pipe = "AiStackprint-test-" + Guid.NewGuid().ToString("N")[..8] };
        _hosts.Add(h);
        h.S.Settings.LimitsEnabled = ["claude-code", "codex-cli"];
        h.S.Settings.TrayEnabled = tray;
        set?.Invoke(h.S.Settings);
        var now = DateTime.Now;
        h.Limits.Seed("claude-code", new(LimitState.Ok, new("Max", [new("5-hour", 75, now.AddHours(2)), new("Weekly", 66, now.AddDays(4))], now), null, null, false));
        h.Limits.Seed("codex-cli", new(LimitState.Ok, new("Plus", [new("Weekly", 21, now.AddDays(4))], now), null, null, false));
        h.Start(tray ? ["--tray"] : []);
        return h;
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void The_widget_shows_each_limit_with_its_bar_and_line_in_both_themes(string theme)
    {
        var h = Host(set: s =>
        {
            s.WidgetEnabled = true;
            s.WidgetOpacity = 70;
        });
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;   // after the host: it applies the settings' theme
        var w = Assert.IsType<UsageWidget>(h.Widget);
        w.Width = Widths.Widget;
        Dispatcher();
        var texts = Ui.Texts(w);
        Assert.Contains("Claude Code", texts);
        Assert.Contains("Max", texts);
        Assert.Contains("Codex", texts.Select(t => t.Split(' ')[0]));
        Assert.Contains("75%", texts);
        Assert.Contains("66%", texts);
        Assert.Contains(texts, t => t.StartsWith("Usage limits · now ", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Full about ", StringComparison.Ordinal));   // 75 % well before its reset: it warns
        Assert.Equal(3, w.GetVisualDescendantsOf<WidgetBar>().Count);
        if (_out != null)
        {
            Directory.CreateDirectory(_out);
            w.CaptureRenderedFrame()?.Save(Path.Combine(_out, $"feature-tray-widget-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }

        h.Quit();
    }

    [AvaloniaFact]
    public void A_failed_reading_keeps_the_last_one_and_says_when_it_was_taken()
    {
        var h = Host(set: s => s.WidgetEnabled = true);
        var old = h.Limits.Status("claude-code").Data!;
        h.Limits.Seed("claude-code", new(LimitState.Offline, old, null, "Cannot reach the provider (offline?).", false));
        Dispatcher();
        var texts = Ui.Texts(h.Widget!);
        Assert.Contains(texts, t => t.StartsWith("as of ", StringComparison.Ordinal));
        Assert.Contains("Cannot reach the provider (offline?).", texts);
        h.Quit();
    }

    [AvaloniaFact]
    public void The_settings_write_which_limits_show_the_opacity_and_the_switches()
    {
        var h = Host();
        var changed = 0;
        var panel = TraySettings.Widget(h.S, h.Limits, () => changed++);
        var win = Ui.Show(panel);
        var boxes = panel.GetVisualDescendantsOf<CheckBox>();
        boxes.First(b => b.Content as string == "5-hour").IsChecked = false;
        Assert.Equal(["Weekly"], h.S.Settings.WidgetShow["claude-code"]);
        boxes.First(b => b.Content as string == "Codex CLI").IsChecked = false;
        Assert.Empty(h.S.Settings.WidgetShow["codex-cli"]);
        panel.GetVisualDescendantsOf<Slider>()[0].Value = 55;
        Assert.Equal(55, h.S.Settings.WidgetOpacity);
        var switches = panel.GetVisualDescendantsOf<ToggleSwitch>();
        switches[0].IsChecked = !h.S.Settings.WidgetOnTop;
        Assert.False(h.S.Settings.WidgetOnTop);
        Assert.True(changed >= 4);
        Assert.Contains("widgetOpacity", File.ReadAllText(Path.Combine(DataFolder.Root, "settings.json")));
        win.Close();
        h.Quit();
    }

    [AvaloniaFact]
    public void The_tray_menu_lists_its_items_and_they_act()
    {
        var h = Host();
        var menu = h.Tray!.CurrentMenu!;
        var items = menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header).ToList();
        Assert.Equal(["Claude Code · 5-hour 75%, weekly 66%", "Open AI Stackprint", "Show widget", "Pause alerts for 1 hour", "Settings", "Quit AI Stackprint"], items);
        Assert.Equal(2, menu.Items.OfType<NativeMenuItemSeparator>().Count());

        Run(h, "Show widget");
        Assert.True(h.S.Settings.WidgetEnabled);
        Assert.NotNull(h.Widget);
        Run(h, "Pause alerts for 1 hour");
        Assert.NotNull(h.AlertsPaused);
        Assert.StartsWith("Resume alerts (paused until ", Header(h, 3));
        Run(h, "Settings");
        Assert.NotNull(h.Main);
        Assert.False(h.Main.InTray);
        Run(h, "Quit AI Stackprint");
        Assert.True(h.Quitting);
        Assert.Equal(1, _life.Shutdowns);
    }

    [AvaloniaFact]
    public void Closing_the_window_hides_it_in_the_tray_and_the_notice_shows_once()
    {
        var h = Host(tray: true);
        h.Open(null);
        var main = h.Main!;
        main.Close();   // programmatic: a script closing it quits
        Assert.True(h.Quitting);

        var again = Host(tray: true);
        again.Open(null);
        Assert.NotNull(again.WentToTray());
        Assert.True(again.S.State.TrayNoticeShown);
        Assert.Null(again.WentToTray());
        again.Main!.ToTray();
        Assert.True(again.Main.InTray);
        Assert.True(again.KeepsRunning);
        again.Open("Usage");
        Assert.False(again.Main.InTray);
        again.Quit();
    }

    [AvaloniaFact]
    public void With_the_tray_off_the_window_quits_and_nothing_stays_behind()
    {
        var h = Host(tray: false);
        Assert.Null(h.Tray);
        Assert.False(h.KeepsRunning);
        Assert.Equal(ShutdownMode.OnMainWindowClose, _life.ShutdownMode);
        Assert.False(_login.IsOn);
        h.Main!.Close();
        Assert.True(h.Quitting);
    }

    [AvaloniaFact]
    public void Start_at_login_writes_the_entry_only_while_the_tray_is_on()
    {
        var h = Host(set: s => s.StartAtLogin = true);
        Assert.True(_login.IsOn);
        Assert.Equal(ShutdownMode.OnExplicitShutdown, _life.ShutdownMode);
        h.S.Settings.TrayEnabled = false;
        h.Apply();
        Assert.False(_login.IsOn);
        Assert.Null(h.Tray);
        h.Quit();
    }

    [AvaloniaFact]
    public void A_limit_crossing_its_threshold_sends_one_alert_through_the_notifier()
    {
        var h = Host();
        Dispatcher();
        _notes.Sent.Clear();
        var now = DateTime.Now;
        h.Limits.Seed("claude-code", new(LimitState.Ok, new("Max", [new("5-hour", 96, now.AddHours(2)), new("Weekly", 66, now.AddDays(4))], now.AddSeconds(5)), null, null, false));
        Dispatcher();
        Assert.Contains(_notes.Sent, a => a.Kind == AlertKind.ShortNearlyFull && a.Title == "Claude Code: 5-hour limit at 96%");
        var count = _notes.Sent.Count;
        h.Limits.Seed("claude-code", h.Limits.Status("claude-code"));
        Dispatcher();
        Assert.Equal(count, _notes.Sent.Count);   // at most once
        Assert.True(_notes.Registered);
        h.Quit();
    }

    [AvaloniaFact]
    public void A_second_start_and_a_notification_button_reach_the_running_copy()
    {
        Assert.Equal("show", SingleInstance.Message([]));
        Assert.Equal("show App:claude-code", SingleInstance.Message(["aistackprint:open/App:claude-code"]));
        Assert.Equal("pause", SingleInstance.Message([Notifier.PauseLink]));
        Assert.Equal("show Usage", SingleInstance.Message(["--open", "Usage"]));
        var pipe = "AiStackprint-test-" + Guid.NewGuid().ToString("N")[..8];
        using var stop = new CancellationTokenSource();
        string? got = null;
        var listen = SingleInstance.Listen(pipe, m => got = m, stop.Token);
        Assert.True(SingleInstance.Send(pipe, "show App:codex-cli"));
        Ui.WaitUntil(() => got != null);
        Assert.Equal("show App:codex-cli", got);
        stop.Cancel();
        Assert.False(SingleInstance.Send("AiStackprint-none-" + Guid.NewGuid().ToString("N")[..8], "show"));
        _ = listen;
    }

    [AvaloniaFact]
    public void The_toast_carries_the_alert_and_its_buttons_as_links()
    {
        var xml = Notifier.WindowsToast.Xml(new(AlertKind.ShortNearlyFull, "k", "claude-code", "Claude Code: 5-hour limit at 80%", "It resets at 10:40 & soon.", true, true));
        Assert.Contains("<text>Claude Code: 5-hour limit at 80%</text>", xml);
        Assert.Contains("10:40 &amp; soon.", xml);
        Assert.Contains("arguments=\"aistackprint:open/App:claude-code\"", xml);
        Assert.Contains("arguments=\"aistackprint:pause\"", xml);
        Assert.DoesNotContain("<actions>", Notifier.WindowsToast.Xml(new(AlertKind.Reset, "k", "x", "t", "b", false, false)));
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void The_notice_and_an_alert_pop_up_look_like_the_mockups(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var n = new TrayNotice(() => { });
        n.Show();
        Assert.Contains("Still running in the tray", Ui.Texts(n));
        Assert.Equal(["Got it", "Quit now"], Ui.ButtonTexts(n));
        var alert = Ui.Show(TrayNotice.Card(AlertPopup.Body(new(AlertKind.RunsOutEarly, "k", "claude-code", "Claude Code: weekly limit runs out early",
            "At this pace it's reached Sun 01:40, 2 d 15 h before the reset. Slow to 10.0% a day to last.", true, false), _ => { })));
        alert.Width = Widths.Widget;
        alert.SizeToContent = SizeToContent.Height;   // as the real pop-up
        Dispatcher();
        Assert.Equal(["Open"], Ui.ButtonTexts(alert));
        if (_out != null)
        {
            Directory.CreateDirectory(_out);
            n.CaptureRenderedFrame()?.Save(Path.Combine(_out, $"feature-tray-notice-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            alert.CaptureRenderedFrame()?.Save(Path.Combine(_out, $"feature-tray-alert-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }

        n.Close();
        alert.Close();
    }

    private static void Run(AppHost h, string header) =>
        h.Tray!.CurrentMenu!.Items.OfType<NativeMenuItem>().First(i => i.Header == header || i.Header!.StartsWith(header, StringComparison.Ordinal)).Command!.Execute(null);

    private static string Header(AppHost h, int i) => ((NativeMenuItem)h.Tray!.CurrentMenu!.Items.Where(x => x is NativeMenuItem and not NativeMenuItemSeparator).ElementAt(i)).Header!;

    private static void Dispatcher() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
}

internal static class VisualFind
{
    public static List<T> GetVisualDescendantsOf<T>(this Visual v) => [.. Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v).OfType<T>()];
}
