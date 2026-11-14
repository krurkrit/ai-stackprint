// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Plan and live limits: a card on the app page and a strip on the dashboard. The last reading stays on screen while a refresh runs.</summary>
internal static class LimitsView
{
    public static bool Has(AppDefinition d) => LimitProviders.Find(d.Limits) != null;

    /// <summary>App page card; nothing for apps without a provider.</summary>
    public static Control Card(Ctx x, AppDefinition d)
    {
        if (!Has(d))
        {
            return new Panel();
        }

        var host = new ContentControl();
        void Render() => host.Content = Body(x, d, compact: false);
        Render();
        var refresh = IconBtn("arrow_sync", "Refresh now (at most once a minute)", () => x.Limits.Request(d, x.S.Projects, force: true));
        var card = Section("data_pie", "Plan & limits", host, refresh);
        card.AttachedToVisualTree += (_, _) => { MainWindow.LimitsChanged += Render; x.Limits.Request(d, x.S.Projects); };
        card.DetachedFromVisualTree += (_, _) => MainWindow.LimitsChanged -= Render;
        return card;
    }

    /// <summary>Dashboard: one row per installed app with a provider.</summary>
    public static Control Strip(Ctx x)
    {
        var apps = x.S.Defs.Where(d => Has(d) && x.Info(d.Id)?.Installed == true && x.ShownApps.Any(a => a.Id == d.Id)).ToList();
        if (apps.Count == 0)
        {
            return new Panel();
        }

        var host = new StackPanel { Spacing = 14 };
        void Render()
        {
            host.Children.Clear();
            foreach (var d in apps)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), ColumnSpacing = 16 };
                g.Children.Add(new Border { Background = Avalonia.Media.Brushes.Transparent, Cursor = Hand, Child = AppName(d.Logo, d.Category, d.Name, 24), VerticalAlignment = VerticalAlignment.Top }
                    .With(b => b.PointerReleased += (_, _) => x.Go("App:" + d.Id)));
                g.Children.Add(Body(x, d, compact: true).With(c => Grid.SetColumn(c, 1)));
                host.Children.Add(g);
            }
        }
        Render();
        var card = Section("data_pie", "Plan & limits", host);
        card.AttachedToVisualTree += (_, _) => { MainWindow.LimitsChanged += Render; foreach (var d in apps) { x.Limits.Request(d, x.S.Projects); } };
        card.DetachedFromVisualTree += (_, _) => MainWindow.LimitsChanged -= Render;
        return card;
    }

    private static StackPanel Body(Ctx x, AppDefinition d, bool compact)
    {
        var st = x.Limits.Status(d.Id);
        var on = x.Limits.Enabled(d.Id);
        var sp = new StackPanel { Spacing = compact ? 6 : 10 };
        if (st.Data == null)
        {
            if (st.Busy) { sp.Children.Add(Busy.Loading($"Asking {LimitProviders.Find(d.Limits)!.Host}…")); return sp; }
            if (!on)
            {
                sp.Children.Add(Row(Txt("Live limits are off for this app.", 13, false, Muted), Btn("Turn on…", () => Consent(x, d), false, "data_pie").With(b => b.MinHeight = 30)));
                return sp;
            }
        }
        if (st.Data is { } s)
        {
            var head = new List<Control>();
            if (s.Plan != null)
            {
                head.Add(Chip(s.Plan, AccentKeyForPlan));
            }

            if (s.FromLogs)
            {
                head.Add(Txt("from the app's own log", 12, false, Muted));
            }

            if (head.Count > 0)
            {
                sp.Children.Add(Row([.. head]));
            }

            foreach (var w in s.Windows)
            {
                sp.Children.Add(Window(w, st.Previous, s, compact));
            }

            if (s.Windows.Count == 0)
            {
                sp.Children.Add(Txt(s.Note ?? "No limits reported.", 13, false, Muted));
            }
            else if (s.Note != null && !compact)
            {
                sp.Children.Add(Txt(s.Note, 12, false, Muted));
            }
        }
        if (st.Message != null && st.State != LimitState.Ok)
        {
            sp.Children.Add(Row(Icon(st.State is LimitState.Off ? "info" : "warning", 14).Themed(TextElement.ForegroundProperty, st.State is LimitState.Off ? Muted : WarningKey), Txt(st.Message, 12, false, Muted)));
        }

        if (x.Limits.Fetched(d.Id) is { } at)
        {
            sp.Children.Add(Busy.Inline(st.Busy, $"Updated {Ago(at)}" + (on ? "" : " · live calls off")));
        }

        return sp;
    }

    private const string AccentKeyForPlan = Accent;

    /// <summary>One window: name, percent used, reset, and when it fills at the current pace.</summary>
    private static StackPanel Window(LimitWindow w, LimitSnapshot? prev, LimitSnapshot now, bool compact)
    {
        var pct = Math.Clamp(w.UsedPercent, 0, 100);
        var bar = new Grid { Height = compact ? 6 : 8, ColumnDefinitions = new ColumnDefinitions($"{Math.Max(pct, 0.01)}*,{Math.Max(100 - pct, 0.01)}*") };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(4) }.Themed(Border.BackgroundProperty, pct >= 90 ? ErrorKey : pct >= 75 ? WarningKey : Accent));
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(4) }.Themed(Border.BackgroundProperty, Subtle).With(b => Grid.SetColumn(b, 1)));
        var text = $"{w.UsedPercent:0.#}% used" + (w.ResetsAt is { } r ? $" · resets {Reset(r)}" : "");
        if (prev?.Windows.FirstOrDefault(p => p.Name == w.Name) is { } before && LimitsService.FullAt(before, prev.FetchedAt, w, now.FetchedAt) is { } full)
        {
            text += $" · full about {full:HH:mm} at this pace";
        }

        return new StackPanel { Spacing = 3, Children = { Row(Txt(w.Name, 13, true), Txt(text, 12, false, Muted)), bar } };
    }

    private static string Reset(DateTime t)
    {
        var left = t - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            return "now";
        }

        var span = left.TotalHours >= 24 ? $"{(int)left.TotalDays} d {left.Hours} h" : left.TotalHours >= 1 ? $"{(int)left.TotalHours} h {left.Minutes} min" : $"{Math.Max(1, left.Minutes)} min";
        return $"in {span} ({(t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("ddd HH:mm"))})";
    }

    public static string Ago(DateTime utc)
    {
        var s = (DateTime.UtcNow - utc).TotalSeconds;
        return s < 60 ? $"{Math.Max(1, (int)s)} s ago" : s < 3600 ? $"{(int)(s / 60)} min ago" : $"{(int)(s / 3600)} h ago";
    }

    /// <summary>Explains what turning on live limits does, then turns it on for this app.</summary>
    public static async void Consent(Ctx x, AppDefinition d)
    {
        var p = LimitProviders.Find(d.Limits)!;
        var min = (int)x.Limits.Interval.TotalMinutes;
        var ok = await Dialogs.Confirm(x.Owner, $"Live limits for {d.Name}",
            $"To show your plan and limits, AI Stackprint reads the sign-in {d.Name} saved on this computer and sends it only to {p.Host}, as {d.Name} itself does, " +
            $"at most once every {min} minute{(min == 1 ? "" : "s")}.\n\nThe token stays in memory; it is never shown, saved or logged, and never renewed by this app.\n\n" +
            "This endpoint is not documented by the provider and may stop working, and using it from another app may be against the provider's terms.", "Turn on");
        if (!ok)
        {
            return;
        }

        if (!x.S.Settings.LimitsEnabled.Contains(d.Id))
        {
            x.S.Settings.LimitsEnabled.Add(d.Id);
        }

        SettingsStore.Save(x.S.Settings);
        x.Limits.Request(d, x.S.Projects, force: true);
        x.Refresh();
    }

    /// <summary>Settings › Usage limits: refresh interval and one switch per app.</summary>
    public static Control Settings(Ctx x)
    {
        var st = x.S.Settings;
        var every = new NumericUpDown { Minimum = 1, Maximum = 60, Increment = 1, FormatString = "0", MinWidth = 120, Value = Math.Clamp(st.LimitsRefreshMinutes, 1, 60) };
        every.ValueChanged += (_, e) => { if (e.NewValue is { } v) { st.LimitsRefreshMinutes = (int)Math.Clamp(Math.Round(v), 1, 60); SettingsStore.Save(st); } };
        var rows = x.S.Defs.Where(Has).OrderBy(d => d.Name).Select(d =>
        {
            var sw = new ToggleSwitch { IsChecked = st.LimitsEnabled.Contains(d.Id), OnContent = "On", OffContent = "Off" };
            sw.IsCheckedChanged += (_, _) =>
            {
                if (sw.IsChecked == true && !st.LimitsEnabled.Contains(d.Id)) { sw.IsChecked = false; Consent(x, d); return; }
                if (sw.IsChecked != true) { st.LimitsEnabled.Remove(d.Id); SettingsStore.Save(st); }
            };
            return new TableRow([AppName(d.Logo, d.Category, d.Name), Txt(LimitProviders.Find(d.Limits)!.Host, 13, false, Muted), sw]);
        });
        return Section("data_pie", "Usage limits", new StackPanel
        {
            Spacing = 12,
            Children =
        {
            Txt("Plan and limits from each app's own sign-in, asked from the provider the app uses. Off until you turn it on per app. Codex limits also come from its session log without any call.", 13, false, Muted),
            Row(Txt("Refresh every", 14, false, Muted), every, Txt("minutes (at least 1); pages show the last reading in between", 12, false, Muted)),
            Table.Build([new Col("App", "2*"), new Col("Asks", "1.4*"), new Col("Live limits", "1*")], rows)
        }
        });
    }
}
