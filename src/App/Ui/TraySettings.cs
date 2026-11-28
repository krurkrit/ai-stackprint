// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The widget's settings (which limits it shows, background opacity, always on top, show at login), shared by the widget's gear pop-up and
/// Settings › Usage limits › Tray and widget. Each change is saved at once and then applied by the callback.
/// </summary>
internal static class TraySettings
{
    public static StackPanel Widget(AppState s, LimitsService limits, Action changed)
    {
        var st = s.Settings;
        void Save()
        {
            SettingsStore.Save(st);
            changed();
        }

        var sp = new StackPanel { Spacing = Space.XS };
        sp.Children.Add(Label("Show", top: false));
        var apps = st.LimitsEnabled.Select(s.Def).OfType<AppDefinition>().Where(LimitsView.Has).ToList();
        if (apps.Count == 0)
        {
            sp.Children.Add(Txt("Turn on live limits for an app first.", TextSize.Small, false, Muted));
        }

        foreach (var d in apps)
        {
            var windows = limits.Status(d.Id).Data?.Windows.Select(w => w.Name).ToList() ?? [];
            var shown = windows.Where(w => WidgetRows.Shows(st, d.Id, w)).ToList();
            var app = Check(d.Name, !st.WidgetShow.TryGetValue(d.Id, out var list) || list.Count > 0, on =>
            {
                if (on)
                {
                    st.WidgetShow.Remove(d.Id);
                }
                else
                {
                    st.WidgetShow[d.Id] = [];
                }

                Save();
            });
            var line = new DockPanel();
            var names = Txt(windows.Count > 0 ? string.Join(", ", windows) : "after the first reading", TextSize.Caption, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center);
            DockPanel.SetDock(names, Dock.Right);
            line.Children.Add(names);
            line.Children.Add(app);
            sp.Children.Add(line);
            var sub = new StackPanel { Margin = new Thickness(Widths.SubOption, 0, 0, 0) };
            foreach (var w in windows)
            {
                sub.Children.Add(Check(w, shown.Contains(w), on =>
                {
                    var set = windows.Where(x => WidgetRows.Shows(st, d.Id, x)).ToHashSet();
                    if (on)
                    {
                        set.Add(w);
                    }
                    else
                    {
                        set.Remove(w);
                    }

                    if (set.Count == windows.Count)
                    {
                        st.WidgetShow.Remove(d.Id);
                    }
                    else
                    {
                        st.WidgetShow[d.Id] = [.. windows.Where(set.Contains)];
                    }

                    app.IsChecked = set.Count > 0;
                    Save();
                }));
            }

            if (sub.Children.Count > 0)
            {
                sp.Children.Add(sub);
            }
        }

        sp.Children.Add(Label("Background opacity", top: true));
        var value = Txt($"{st.WidgetOpacity}%", TextSize.Small).With(t =>
        {
            t.Width = Widths.WidgetPercent;
            t.TextAlignment = Avalonia.Media.TextAlignment.Right;
            t.VerticalAlignment = VerticalAlignment.Center;
        });
        var slider = new Slider
        {
            Minimum = UsageWidget.OpacityMin,
            Maximum = 100,
            TickFrequency = UsageWidget.OpacityStep,
            SmallChange = UsageWidget.OpacityStep,
            LargeChange = UsageWidget.OpacityStep * 2,
            IsSnapToTickEnabled = true,
            Value = Math.Clamp(st.WidgetOpacity, UsageWidget.OpacityMin, 100)
        };
        slider.ValueChanged += (_, e) =>
        {
            st.WidgetOpacity = (int)Math.Round(e.NewValue);
            value.Text = $"{st.WidgetOpacity}%";
            Save();
        };
        var row = new DockPanel();
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(value);
        row.Children.Add(slider);
        sp.Children.Add(row);
        sp.Children.Add(Txt("Text stays solid; under the pointer the widget is opaque.", TextSize.Caption, false, Muted));
        sp.Children.Add(Switch("Always on top", st.WidgetOnTop, on =>
        {
            st.WidgetOnTop = on;
            Save();
        }).With(c => c.Margin = new Thickness(0, Space.S, 0, 0)));
        sp.Children.Add(Switch("Show at login", st.WidgetAtLogin, on =>
        {
            st.WidgetAtLogin = on;
            Save();
        }));
        return sp;
    }

    private static TextBlock Label(string text, bool top) =>
        Txt(text, TextSize.Caption, false, Muted).With(t => t.Margin = new Thickness(0, top ? Space.M : 0, 0, Space.XXS));

    /// <summary>A switch with its label on the left.</summary>
    public static Control Switch(string label, bool on, Action<bool> set)
    {
        var sw = new ToggleSwitch { IsChecked = on, OnContent = null, OffContent = null, MinWidth = 0 };
        sw.IsCheckedChanged += (_, _) => set(sw.IsChecked == true);
        var d = new DockPanel();
        DockPanel.SetDock(sw, Dock.Right);
        d.Children.Add(sw);
        d.Children.Add(Txt(label, TextSize.Small).With(t => t.VerticalAlignment = VerticalAlignment.Center));
        return d;
    }
}
