// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The one loading indicator: a ring of dots with a running head (<see cref="DotsLoader"/>), in the accent color. <see cref="Loading"/> shows at once (pass delayed: true to wait 300 ms for quick work).
/// It turns only while on screen.
/// </summary>
internal static class Busy
{
    /// <summary>Style class on every spinner: a page has data still loading while one is on screen (the --perf run waits for none).</summary>
    internal const string Class = "busy";

    private const int DelayMs = 300;

    /// <summary>The small loading icon beside text and in cards: a <see cref="DotsLoader"/> of <paramref name="size"/>, in the accent color or <paramref name="key"/>.</summary>
    public static Control Spinner(double size = IconSize.S, string key = Accent) => new DotsLoader(Size(size), key);

    /// <summary>Spinner with a muted label, shown at once (<paramref name="delayed"/>: only after 300 ms, for work that is usually quick).</summary>
    public static Control Loading(string text = "Loading…", double size = IconSize.M, bool delayed = false)
    {
        var row = Status(text, out _, size);
        if (!delayed)
        {
            return row;   // shown at once: a component with no data yet always says it is loading
        }

        row.Opacity = 0;
        row.AttachedToVisualTree += (_, _) => DispatcherTimer.RunOnce(() => row.Opacity = 1, TimeSpan.FromMilliseconds(DelayMs));
        return row;
    }

    /// <summary>Spinner with a muted label, shown at once; the label is returned so its text can follow the work ("Checking 45 of 120").</summary>
    public static Control Status(string text, out TextBlock label, double size = IconSize.M)
    {
        label = Txt(text, TextSize.Small, false, Muted);
        var row = Row(Spinner(size), label);
        row.Margin = new Thickness(Space.XXS, Space.S);
        return row;
    }

    /// <summary>Spinner beside a "Updated 40 s ago" style note while a refresh runs; keeps the old data on screen.</summary>
    public static Control Inline(bool busy, string note, string? key = null) =>
        busy ? Row(Spinner(IconSize.XS, key ?? Accent), Txt(note, TextSize.Caption, false, key ?? Muted)) : Txt(note, TextSize.Caption, false, key ?? Muted);

    /// <summary>
    /// Runs <paramref name="work"/> off the UI thread with the spinner in <paramref name="host"/>, then shows the result.
    /// Expected errors (files, network) replace the spinner with the message and a Retry button, never a spinner left turning.
    /// </summary>
    public static void Into<T>(ContentControl host, string label, Func<CancellationToken, T> work, Func<T, Control> show, CancellationToken ct = default) => Fire.Forget(IntoAsync<T>(host, label, work, show, ct));

    private static async Task IntoAsync<T>(ContentControl host, string label, Func<CancellationToken, T> work, Func<T, Control> show, CancellationToken ct = default)
    {
        host.Content = Loading(label);
        try
        {
            var r = await Task.Run(() => work(ct), ct);
            if (!ct.IsCancellationRequested)
            {
                host.Content = Reveal.Fade(show(r));   // the data fades in over the loading icon
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException)
        {
            host.Content = Row(Icon("warning", IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, WarningKey), Txt(e.Message, TextSize.Small, false, Muted),
                Btn("Retry", () => Into(host, label, work, show, ct), false, "arrow_sync"));
        }
    }
}
