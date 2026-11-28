// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Settings › Benchmarks: the switch, the Artificial Analysis API key and Refresh now. The download is <see cref="BenchmarkUpdate"/> in Core; the Models page draws the chart.
/// The key is saved in state.json (machine only, never exported), shown nowhere after it is saved and never logged.
/// </summary>
internal static class BenchmarksSection
{
    private static bool _running;

    /// <summary>The switch is on and a key is saved: the benchmarks may be downloaded.</summary>
    public static bool Ready(Ctx x) => x.S.Settings.BenchmarksEnabled && !string.IsNullOrWhiteSpace(x.S.State.BenchmarkKey);

    /// <summary>Ready, and the last download is a day old or there is none.</summary>
    public static bool Due(Ctx x) => Ready(x) && !_running && BenchmarkUpdate.Due(x.S.State.BenchmarkCheckedUtc, DateTime.UtcNow);

    /// <summary>One download; remembers when and what it found. A failure keeps the copy in use.</summary>
    public static async Task<BenchmarkResult> Fetch(Ctx x)
    {
        _running = true;
        try
        {
            var r = await BenchmarkUpdate.RunAsync(x.S.State.BenchmarkKey ?? "");
            x.S.State.BenchmarkCheckedUtc = DateTime.UtcNow;
            x.S.State.BenchmarkResult = r.Message;
            StateStore.Save(x.S.State);
            return r;
        }
        finally { _running = false; }
    }

    public static void Open(Ctx x, string url) => Fire.Forget(OpenAsync(x, url));

    private static async Task OpenAsync(Ctx x, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            await x.Owner.Launcher.LaunchUriAsync(uri);
        }
    }

    public static Control Build(Ctx x)
    {
        var st = x.S.Settings;
        var on = new ToggleSwitch
        {
            IsChecked = st.BenchmarksEnabled,
            OnContent = "Download benchmark scores once a day",
            OffContent = "Download benchmark scores once a day"
        };
        on.IsCheckedChanged += (_, _) =>
        {
            st.BenchmarksEnabled = on.IsChecked == true;
            SettingsStore.Save(st);
        };
        var saved = !string.IsNullOrWhiteSpace(x.S.State.BenchmarkKey);
        var key = new TextBox
        {
            PasswordChar = '•',
            PlaceholderText = saved ? "A key is saved; paste a new one to replace it" : "Artificial Analysis API key",
            Width = Widths.DialogM
        };
        var result = new ContentControl();
        var save = Btn("Save key", () => SaveKey(x, key.Text), false, "save");
        var keyRow = saved ? Row(key, save, Btn("Remove key", () => RemoveKey(x), false, "delete")) : Row(key, save);

        var last = x.S.State.BenchmarkCheckedUtc is { } at ? $"Last download {at.ToLocalTime():yyyy-MM-dd HH:mm}: {x.S.State.BenchmarkResult}" : "Not downloaded yet.";
        return Section("data_pie", "Benchmarks", new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                Txt($"The Benchmarks chart on the Models page compares models on intelligence, professional skills (engineering, finance, legal, strategy), speed, first token and cost per task. The scores come from Artificial Analysis ({BenchmarkUpdate.Host}), " +
                    "which asks for credit and a free API key. Off until you turn it on and save a key; a download is at most once a day, and only the key is sent, to that host only. " +
                    "The key stays in state.json on this computer: it is never shown again, logged or exported.", TextSize.Small, false, Muted),
                Row(Btn("Artificial Analysis", () => Open(x, BenchmarkUpdate.Site + "/data-api"), false, "open"), Txt("Free keys are made in your account there.", TextSize.Small, false, Muted)),
                on,
                keyRow,
                Txt(saved ? "A key is saved." : "No key saved.", TextSize.Small, false, Muted),
                Row(Btn("Refresh now", async () =>
                {
                    if (!Ready(x))
                    {
                        result.Content = Txt("Turn the switch on and save a key first.", TextSize.Small, false, Muted);
                        return;
                    }

                    result.Content = Busy.Loading($"Asking {BenchmarkUpdate.Host}…");
                    var r = await Fetch(x);
                    result.Content = r.Ok
                        ? Txt(r.Message, TextSize.Small, false, Muted)
                        : Row(Icon("warning", IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, WarningMark), Txt(r.Message, TextSize.Small, false, Muted));
                }, false, "arrow_sync"), result),
                Txt(last, TextSize.Small, false, Muted)
            }
        });
    }

    private static void SaveKey(Ctx x, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            x.S.State.BenchmarkKey = text.Trim();
            StateStore.Save(x.S.State);
            x.Refresh();
        }
    }

    private static void RemoveKey(Ctx x)
    {
        x.S.State.BenchmarkKey = null;
        StateStore.Save(x.S.State);
        x.Refresh();
    }
}
