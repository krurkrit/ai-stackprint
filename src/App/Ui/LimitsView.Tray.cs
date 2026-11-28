// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Settings › Usage limits: the "Tray and widget" and "Alerts" cards.</summary>
internal static partial class LimitsView
{
    private const int MaxAlertHours = 72;

    private static Control TrayCard(Ctx x)
    {
        var st = x.S.Settings;
        void Save()
        {
            SettingsStore.Save(st);
            x.Host?.Apply();
        }

        var sp = new StackPanel
        {
            Spacing = Space.M,
            Children =
            {
                Txt("Keeps checking usage limits with the window closed: the tray icon shows the most-used limit, and the widget and alerts go on. " +
                    "Off until you turn it on; Quit is in the tray icon's menu.", TextSize.Small, false, Muted),
                TraySettings.Switch("Keep running in the tray", st.TrayEnabled, on =>
                {
                    st.TrayEnabled = on;
                    Save();
                    x.Refresh();
                }),
            }
        };
        if (st.TrayEnabled)
        {
            sp.Children.Add(TraySettings.Switch("× hides the window instead of quitting", st.CloseToTray, on =>
            {
                st.CloseToTray = on;
                Save();
            }));
            sp.Children.Add(TraySettings.Switch("Start in the tray at login", st.StartAtLogin, on =>
            {
                st.StartAtLogin = on;
                Save();
            }));
        }

        sp.Children.Add(TraySettings.Switch("Show the widget", st.WidgetEnabled, on =>
        {
            if (x.Host is { } h)
            {
                h.ShowWidget(on);
            }
            else
            {
                st.WidgetEnabled = on;
                SettingsStore.Save(st);
            }
        }));
        sp.Children.Add(TraySettings.Widget(x.S, x.Limits, () => x.Host?.Apply()));
        if (OperatingSystem.IsLinux())
        {
            sp.Children.Add(Txt("On Linux the tray icon needs a StatusNotifierItem host: KDE has one; GNOME needs the AppIndicator extension.", TextSize.Caption, false, Muted));
        }

        return Section("window", "Tray and widget", sp);
    }

    private static Control AlertsCard(Ctx x)
    {
        var st = x.S.Settings;
        void Save()
        {
            SettingsStore.Save(st);
            x.Host?.Apply();
        }

        var at = new TextBox { Text = string.Join(", ", st.AlertShortAt), Width = Widths.FieldS, PlaceholderText = "none" };
        at.LostFocus += (_, _) =>
        {
            st.AlertShortAt = [.. (at.Text ?? "").Split(',', ' ', ';').Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0)
                .Where(v => v is > 0 and <= 100).Distinct().Order()];
            at.Text = string.Join(", ", st.AlertShortAt);
            Save();
        };
        return Section("warning", "Alerts", new StackPanel
        {
            Spacing = Space.M,
            Children =
            {
                Txt("Notifications on this computer only, nothing is sent anywhere. They come while the tray is on or the window is open, at most once each; Do Not Disturb applies.", TextSize.Small, false, Muted),
                TraySettings.Switch("Send alerts", st.AlertsEnabled, on =>
                {
                    st.AlertsEnabled = on;
                    Save();
                }),
                Row(Txt("Short window (5-hour) at", TextSize.Small, false, Muted), at, Txt("percent, crossed upward (empty = off)", TextSize.Caption, false, Muted)),
                Row(Txt("Runs out early: at least", TextSize.Small, false, Muted), Num(st.AlertEarlyHours, 0, MaxAlertHours, v => { st.AlertEarlyHours = v; Save(); }),
                    Txt("hours before the reset (0 = off)", TextSize.Caption, false, Muted)),
                TraySettings.Switch("When the weekly window resets", st.AlertReset, on =>
                {
                    st.AlertReset = on;
                    Save();
                }),
                Row(Txt("Usage left:", TextSize.Small, false, Muted), Num(st.AlertLeftHours, 0, MaxAlertHours, v => { st.AlertLeftHours = v; Save(); }),
                    Txt("hours before the reset, when more than", TextSize.Caption, false, Muted), Num(st.AlertLeftPercent, 0, 100, v => { st.AlertLeftPercent = v; Save(); }),
                    Txt("% would be left (0 hours = off)", TextSize.Caption, false, Muted))
            }
        });
    }

    private static NumericUpDown Num(int value, int min, int max, Action<int> set)
    {
        var box = W.Stepper(new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Value = Math.Clamp(value, min, max) }, "000");
        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } v)
            {
                set((int)Math.Clamp(Math.Round(v), min, max));
            }
        };
        return box;
    }
}
