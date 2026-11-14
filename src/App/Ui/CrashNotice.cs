// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// After a crash, a card at the bottom right: report it on GitHub (opens a prefilled new-issue page; the user reviews and submits it there),
/// open crash.log, or close. Nothing is sent by the app. The link is built by <see cref="CrashReport"/> in Core.
/// </summary>
internal static class CrashNotice
{
    private static Border? _card;

    public static void Show(Ctx x)
    {
        Toast.Unpin(_card);
        _card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10),
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Row(Icon("warning", 16).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, WarningKey), Txt("AI Stackprint closed unexpectedly last time", 13, true)),
                    Txt("Report opens a new GitHub issue in your browser with the error from crash.log (your home folder and user name removed). You can read and edit it before you submit; nothing is sent until you do.", 12, false, Muted),
                    Row(Btn("Report on GitHub", () => { Report(x); Toast.Unpin(_card); }, true, "open"),
                        Btn("Open crash.log", () => PathUtil.OpenFile(CrashLog.File)),
                        Btn("Close", () => Toast.Unpin(_card)))
                }
            }
        }.Themed(Border.BackgroundProperty, Surface).Themed(Border.BorderBrushProperty, BorderKey);
        Toast.Pin(_card);
    }

    private static async void Report(Ctx x)
    {
        if (CrashReport.ForLastCrash() is not { } url)
        {
            Toast.Show("crash.log has no entry to report.", "warning", 6);
            return;
        }

        if (!await x.Owner.Launcher.LaunchUriAsync(new Uri(url)))
        {
            Toast.Show("Could not open the browser. The details are in " + CrashLog.File, "warning", 8);
        }
    }
}
