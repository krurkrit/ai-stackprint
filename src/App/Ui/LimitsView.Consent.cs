// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Avalonia.Controls;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class LimitsView
{
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
    public static void Consent(Ctx x, AppDefinition d) => Fire.Forget(ConsentAsync(x, d));

    private static async Task ConsentAsync(Ctx x, AppDefinition d)
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
        var every = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 60,
            Increment = 1,
            FormatString = "0",
            MinWidth = Widths.FieldS,
            Value = Math.Clamp(st.LimitsRefreshMinutes, 1, 60)
        };
        every.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } v)
            {
                st.LimitsRefreshMinutes = (int)Math.Clamp(Math.Round(v), 1, 60);
                SettingsStore.Save(st);
            }
        };
        var rows = x.S.Defs.Where(Has).OrderBy(d => d.Name).Select(d =>
        {
            var sw = new ToggleSwitch
            {
                IsChecked = st.LimitsEnabled.Contains(d.Id),
                OnContent = "On",
                OffContent = "Off"
            };
            sw.IsCheckedChanged += (_, _) =>
            {
                if (sw.IsChecked == true && !st.LimitsEnabled.Contains(d.Id))
                {
                    sw.IsChecked = false;
                    Consent(x, d);
                    return;
                }
                if (sw.IsChecked != true)
                {
                    st.LimitsEnabled.Remove(d.Id);
                    SettingsStore.Save(st);
                }
            };
            return new TableRow([AppName(d.Logo, d.Category, d.Name), Txt(LimitProviders.Find(d.Limits)!.Host, TextSize.Small, false, Muted), sw]);
        });
        return Section("data_pie", "Usage limits", new StackPanel
        {
            Spacing = Space.L,
            Children =
        {
            Txt("Plan and limits from each app's own sign-in, asked from the provider the app uses. Off until you turn it on per app. Codex limits also come from its session log without any call.", TextSize.Small, false, Muted),
            Row(Txt("Refresh every", TextSize.Body, false, Muted), every, Txt("minutes (at least 1); pages show the last reading in between", TextSize.Caption, false, Muted)),
            Table.Build([new Col("App", "2*"), new Col("Asks", "1.4*"), new Col("Live limits", "1*")], rows)
        }
        });
    }
}
