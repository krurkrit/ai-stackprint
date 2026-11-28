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
internal static partial class LimitsView
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
        card.AttachedToVisualTree += (_, _) =>
        {
            MainWindow.LimitsChanged += Render;
            x.Limits.Request(d, x.S.Projects);
        };
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

        var host = new StackPanel { Spacing = Space.XL };
        void Render()
        {
            host.Children.Clear();
            foreach (var d in apps)
            {
                var g = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions($"{Widths.LabelL},*"),
                    ColumnSpacing = Space.XL
                };
                g.Children.Add(new Border
                {
                    Background = Avalonia.Media.Brushes.Transparent,
                    Cursor = Hand,
                    Child = AppName(d.Logo, d.Category, d.Name, Sizes.LogoS),
                    VerticalAlignment = VerticalAlignment.Top
                }
                    .With(b => b.PointerReleased += (_, _) => x.Go("App:" + d.Id)));
                g.Children.Add(Body(x, d, compact: true).With(c => Grid.SetColumn(c, 1)));
                host.Children.Add(g);
            }
        }
        Render();
        var card = Section("data_pie", "Usage", host);
        card.AttachedToVisualTree += (_, _) =>
        {
            MainWindow.LimitsChanged += Render;
            foreach (var d in apps) { x.Limits.Request(d, x.S.Projects); }
        };
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
            if (st.Busy)
            {
                sp.Children.Add(Busy.Loading($"Asking {LimitProviders.Find(d.Limits)!.Host}…"));
                return sp;
            }
            if (!on)
            {
                sp.Children.Add(Row(Txt("Live limits are off for this app.", TextSize.Small, false, Muted), Btn("Turn on…", () => Consent(x, d), false, "data_pie").With(b => b.MinHeight = Sizes.CompactControl)));
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
                head.Add(Txt("from the app's own log", TextSize.Caption, false, Muted));
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
                sp.Children.Add(Txt(s.Note ?? "No limits reported.", TextSize.Small, false, Muted));
            }
            else if (s.Note != null && !compact)
            {
                sp.Children.Add(Txt(s.Note, TextSize.Caption, false, Muted));
            }

            if (!compact && Pace(x, d, s) is { } pace)
            {
                sp.Children.Add(pace);
            }
        }
        if (st.Message != null && st.State != LimitState.Ok)
        {
            sp.Children.Add(Row(Icon(st.State is LimitState.Off ? "info" : "warning", IconSize.S).Themed(TextElement.ForegroundProperty, st.State is LimitState.Off ? Muted : WarningMark), Txt(st.Message, TextSize.Caption, false, Muted)));
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
        var bar = new Grid
        {
            Height = compact ? 6 : 8,
            ColumnDefinitions = new ColumnDefinitions($"{Math.Max(pct, 0.01)}*,{Math.Max(100 - pct, 0.01)}*")
        };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(Radius.S) }.Themed(Border.BackgroundProperty, pct >= 90 ? ErrorMark : pct >= 75 ? WarningMark : Accent));
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(Radius.S) }.Themed(Border.BackgroundProperty, Subtle).With(b => Grid.SetColumn(b, 1)));
        var text = $"{w.UsedPercent:0.#}% used" + (w.ResetsAt is { } r ? $" · resets {Reset(r)}" : "");
        if (prev?.Windows.FirstOrDefault(p => p.Name == w.Name) is { } before && LimitsService.FullAt(before, prev.FetchedAt, w, now.FetchedAt) is { } full)
        {
            text += $" · full about {full:HH:mm} at this pace";
        }

        return new StackPanel
        {
            Spacing = Space.XS,
            Children = { Row(Txt(w.Name, TextSize.Small, true), Txt(text, TextSize.Caption, false, Muted)), bar }
        };
    }
}
