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
/// The one loading indicator: a turning arc in the accent color. <see cref="Loading"/> appears only after 300 ms,
/// so quick work never flashes it. It turns only while on screen.
/// </summary>
internal static class Busy
{
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

    public static Control Spinner(double size = IconSize.S)
    {
        var arc = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M12,3 A9,9 0 1 1 3,12"), StrokeThickness = ArcWidth, StrokeLineCap = PenLineCap.Round }
            .Themed(Avalonia.Controls.Shapes.Shape.StrokeProperty, Accent);
        var v = new Viewbox
        {
            Width = Size(size),
            Height = Size(size),
            Child = new Canvas { Width = Sizes.IconBox, Height = Sizes.IconBox, Children = { arc } },
            RenderTransform = new RotateTransform(),
            VerticalAlignment = VerticalAlignment.Center
        };
        CancellationTokenSource? cts = null;
        v.AttachedToVisualTree += (_, _) => { cts?.Cancel(); cts = new(); _ = Spin(v, cts); };
        v.DetachedFromVisualTree += (_, _) => { cts?.Cancel(); cts = null; };   // Spin disposes it
        return v;
    }

    /// <summary>Spinner with a muted label, shown after a short delay.</summary>
    public static Control Loading(string text = "Loading…", double size = IconSize.M)
    {
        var row = Row(Spinner(size), Txt(text, TextSize.Small, false, Muted));
        row.Opacity = 0;
        row.Margin = new Thickness(Space.XXS, Space.S);
        row.AttachedToVisualTree += (_, _) => DispatcherTimer.RunOnce(() => row.Opacity = 1, TimeSpan.FromMilliseconds(DelayMs));
        return row;
    }

    /// <summary>Spinner beside a "Updated 40 s ago" style note while a refresh runs; keeps the old data on screen.</summary>
    public static Control Inline(bool busy, string note) =>
        busy ? Row(Spinner(IconSize.XS), Txt(note, TextSize.Caption, false, Muted)) : Txt(note, TextSize.Caption, false, Muted);

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
                host.Content = show(r);
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
