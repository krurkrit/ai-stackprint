// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Settings › Updates (switch, Check now) and the "new version" notice. The check itself is <see cref="UpdateCheck"/> in Core.</summary>
internal static class UpdateView
{
    private static Border? _notice;

    public static Control Settings(Ctx x)
    {
        var st = x.S.Settings;
        var daily = new ToggleSwitch { IsChecked = st.CheckUpdates, OnContent = "Check once a day", OffContent = "Check once a day" };
        daily.IsCheckedChanged += (_, _) =>
        {
            st.CheckUpdates = daily.IsChecked == true;
            SettingsStore.Save(st);
            if (st.CheckUpdates)
            {
                Startup(x);
            }
        };
        var result = new ContentControl();
        return Section("arrow_sync", "Updates", new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                Txt($"This is version {UpdateCheck.Label}. A check asks {UpdateCheck.Host} for the latest release of {UpdateCheck.Repo}; nothing about you or this computer is sent. " +
                    "Off until you turn it on. New versions are downloaded from the release page, not by this app.", 13, false, Muted),
                daily,
                Row(Btn("Check now", () => CheckNow(x, result), false, "arrow_sync"), result)
            }
        });
    }

    /// <summary>After the first scan: checks when the daily check is on and due, and shows the notice for a newer version not put off with Later.</summary>
    public static async void Startup(Ctx x)
    {
        var st = x.S.Settings;
        if (!st.CheckUpdates || !UpdateCheck.Due(st.UpdateCheckedUtc, DateTime.UtcNow))
        {
            return;
        }

        UpdateCheck.Release? newer;
        try { newer = UpdateCheck.Newer(await UpdateCheck.LatestAsync(), UpdateCheck.Current); }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException) { return; }   // quiet; tried again next start
        st.UpdateCheckedUtc = DateTime.UtcNow;
        SettingsStore.Save(st);
        if (newer != null && st.UpdateDismissed != newer.Version.ToString())
        {
            Notice(x, newer);
        }
    }

    private static async void CheckNow(Ctx x, ContentControl result)
    {
        result.Content = Busy.Loading("Checking…", 14);
        try
        {
            var latest = await UpdateCheck.LatestAsync();
            var newer = UpdateCheck.Newer(latest, UpdateCheck.Current);
            x.S.Settings.UpdateCheckedUtc = DateTime.UtcNow;
            SettingsStore.Save(x.S.Settings);
            result.Content = newer != null
                ? Row(Txt($"{newer.Version} is available.", TextSize.Small), Btn("Release page", () => Open(x, newer), true, "open"))
                : Txt(latest == null ? "No release has been published yet." : $"You have the latest version ({UpdateCheck.Label}).", TextSize.Small, false, Muted);
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            var why = e is OperationCanceledException ? "GitHub did not answer in time." : e.Message;
            result.Content = Row(Icon("warning", IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, WarningKey),
                Txt("Could not check: " + why, TextSize.Small, false, Muted), Btn("Retry", () => CheckNow(x, result), false, "arrow_sync"));
        }
    }

    /// <summary>Stays at the bottom right until Release page or Later; Later skips this version.</summary>
    private static void Notice(Ctx x, UpdateCheck.Release r)
    {
        Toast.Unpin(_notice);
        _notice = new Border
        {
            CornerRadius = new CornerRadius(Radius.L),
            BorderThickness = new Thickness(Stroke.Thin),
            Padding = new Thickness(Space.XL, Space.L),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new StackPanel
            {
                Spacing = Space.M,
                Children =
                {
                    Row(Icon("arrow_sync", IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Accent), Txt($"AI Stackprint {r.Version} is available", TextSize.Small, true)),
                    Row(Btn("Release page", () => { Open(x, r); Toast.Unpin(_notice); }, true, "open"),
                        Btn("Later", () => { x.S.Settings.UpdateDismissed = r.Version.ToString(); SettingsStore.Save(x.S.Settings); Toast.Unpin(_notice); }))
                }
            }
        }.Themed(Border.BackgroundProperty, Surface).Themed(Border.BorderBrushProperty, BorderKey);
        Toast.Pin(_notice);
    }

    /// <summary>Opens the release page in the browser; <see cref="UpdateCheck.Parse"/> only accepts pages of this project.</summary>
    private static async void Open(Ctx x, UpdateCheck.Release r)
    {
        if (!await x.Owner.Launcher.LaunchUriAsync(new Uri(r.Url)))
        {
            Toast.Show("Could not open the browser. The page is " + r.Url, "warning", 8);
        }
    }
}
