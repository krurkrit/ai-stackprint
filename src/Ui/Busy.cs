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
static class Busy
{
    const int DelayMs = 300;

    static readonly Animation Turn = new()
    {
        Duration = TimeSpan.FromSeconds(0.9),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, 0d) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 360d) } }
        }
    };

    /// <summary>Looping animations cannot use RunAsync, so one turn is run again until the spinner leaves the screen.</summary>
    static async Task Spin(Animatable v, CancellationToken ct)
    {
        try { while (!ct.IsCancellationRequested) await Turn.RunAsync(v, ct); }
        catch (OperationCanceledException) { }
    }

    public static Control Spinner(double size = 16)
    {
        var arc = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M12,3 A9,9 0 1 1 3,12"), StrokeThickness = 2.5, StrokeLineCap = PenLineCap.Round }
            .Themed(Avalonia.Controls.Shapes.Shape.StrokeProperty, Accent);
        var v = new Viewbox { Width = Size(size), Height = Size(size), Child = new Canvas { Width = 24, Height = 24, Children = { arc } },
            RenderTransform = new RotateTransform(), VerticalAlignment = VerticalAlignment.Center };
        CancellationTokenSource? cts = null;
        v.AttachedToVisualTree += (_, _) => { cts?.Cancel(); cts = new(); _ = Spin(v, cts.Token); };
        v.DetachedFromVisualTree += (_, _) => cts?.Cancel();
        return v;
    }

    /// <summary>Spinner with a muted label, shown after a short delay.</summary>
    public static Control Loading(string text = "Loading…", double size = 18)
    {
        var row = Row(Spinner(size), Txt(text, 13, false, Muted));
        row.Opacity = 0;
        row.Margin = new Thickness(2, 6);
        row.AttachedToVisualTree += (_, _) => DispatcherTimer.RunOnce(() => row.Opacity = 1, TimeSpan.FromMilliseconds(DelayMs));
        return row;
    }

    /// <summary>Spinner beside a "Updated 40 s ago" style note while a refresh runs; keeps the old data on screen.</summary>
    public static Control Inline(bool busy, string note) =>
        busy ? Row(Spinner(12), Txt(note, 12, false, Muted)) : Txt(note, 12, false, Muted);

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
            if (!ct.IsCancellationRequested) host.Content = show(r);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException)
        {
            host.Content = Row(Icon("warning", 16).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, WarningKey), Txt(e.Message, 13, false, Muted),
                Btn("Retry", () => Into(host, label, work, show, ct), false, "arrow_sync"));
        }
    }
}
