// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The one loading indicator: a turning arc in the accent color. <see cref="Loading"/> shows at once (pass delayed: true to wait 300 ms for quick work).
/// It turns only while on screen.
/// </summary>
internal static class Busy
{
    /// <summary>Style class on every spinner: a page has data still loading while one is on screen (the --perf run waits for none).</summary>
    public const string Class = "busy";

    private const int DelayMs = 300;
    private const double ArcWidth = 2.5;   // spinner line, in the 24-unit icon box (scales with the spinner)

    private static readonly Animation _turn = new()
    {
        Duration = TimeSpan.FromSeconds(0.9),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, 0d) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 360d) } }
        }
    };

    /// <summary>Looping animations cannot use RunAsync, so one turn is run again until the spinner leaves the screen.</summary>
    private static async Task Spin(Animatable v, CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await _turn.RunAsync(v, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { cts.Dispose(); }   // the loop owns its source: done only after the last turn stopped
    }

    public static Control Spinner(double size = IconSize.S, string key = Accent)
    {
        var arc = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M12,3 A9,9 0 1 1 3,12"), StrokeThickness = ArcWidth, StrokeLineCap = PenLineCap.Round }
            .Themed(Avalonia.Controls.Shapes.Shape.StrokeProperty, key);
        var v = new Viewbox
        {
            Width = Size(size),
            Height = Size(size),
            Child = new Canvas { Width = Sizes.IconBox, Height = Sizes.IconBox, Children = { arc } },
            RenderTransform = new RotateTransform(),
            VerticalAlignment = VerticalAlignment.Center
        };
        v.Classes.Add(Class);
        CancellationTokenSource? cts = null;
        v.AttachedToVisualTree += (_, _) => { cts?.Cancel(); cts = new(); _ = Spin(v, cts); };
        v.DetachedFromVisualTree += (_, _) => { cts?.Cancel(); cts = null; };   // Spin disposes it
        return v;
    }

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
    public static async void Into<T>(ContentControl host, string label, Func<CancellationToken, T> work, Func<T, Control> show, CancellationToken ct = default)
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
