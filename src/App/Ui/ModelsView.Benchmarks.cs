// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The Benchmarks radar of the Models page: the models in the slots on Artificial Analysis scores, speed and cost (<see cref="BenchmarkCompare"/>).</summary>
internal partial class ModelsView
{
    /// <summary>The second radar card, under the first; a hint with a button when benchmarks are off or not downloaded yet.</summary>
    private Control BenchmarkCard(List<ModelRow> models)
    {
        if (!BenchmarksSection.Ready(x))
        {
            return Section("data_pie", "Benchmarks", Hint("Compare these models on intelligence, professional skills, speed and cost per task with scores from Artificial Analysis. Turn it on and save a free API key in Settings › Benchmarks.",
                Btn("Open Settings", () => x.Go("Settings:Benchmarks"), false, "settings")));
        }

        var rows = BenchmarkStore.Load();
        if (rows.Count == 0)
        {
            var again = new ContentControl();
            return Section("data_pie", "Benchmarks", Hint(x.S.State.BenchmarkResult is { Length: > 0 } why ? "No benchmark scores yet. " + why : "No benchmark scores yet.",
                Btn("Download now", async () =>
                {
                    again.Content = Busy.Loading($"Asking {BenchmarkUpdate.Host}…");
                    await BenchmarksSection.Fetch(x);
                    x.Refresh();
                }, true, "arrow_sync"), again));
        }

        var r = BenchmarkCompare.Score([.. models.Select(m => m.Key)], [.. _rows.Select(m => m.Key)], rows);
        if (r.Scores.Count == 0)
        {
            return Section("data_pie", "Benchmarks", Hint("Artificial Analysis has no scores for these models: " + string.Join(", ", r.Missing) + ".", null));
        }

        var legend = new StackPanel
        {
            Spacing = Space.M,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            MinWidth = Widths.LabelL
        };
        foreach (var (s, i) in r.Scores.Select((s, i) => (s, i)))
        {
            var color = RadarChart.SeriesKeys[_picked.IndexOf(s.Key) % RadarChart.SeriesKeys.Length];
            var dot = new Border
            {
                Width = Sizes.Dot,
                Height = Sizes.Dot,
                CornerRadius = new Avalonia.CornerRadius(Radius.Pill)
            }.Themed(Border.BackgroundProperty, color);
            legend.Children.Add(Row(dot, Txt(s.Key, TextSize.Body, true), Txt(r.Variants[i], TextSize.Small, false, Muted)));
        }

        if (r.Missing.Count > 0)
        {
            legend.Children.Add(Txt("No scores for " + string.Join(", ", r.Missing) + ".", TextSize.Caption, false, Muted));
        }

        legend.Children.Add(Txt("Each axis runs from the worst (0%) to the best (100%) of all models on Artificial Analysis. Where a model was run at several efforts, its best run is shown. Hover a corner for the real value.",
            TextSize.Caption, false, Muted).With(t => t.MaxWidth = Widths.LabelL * 1.5));
        legend.Children.Add(Btn("Data: Artificial Analysis", () => BenchmarksSection.Open(x, BenchmarkUpdate.Site), false, "open"));
        var body = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = Space.XXL,
            Children = { RadarChart.Build(BenchmarkCompare.Axes, r.Scores, [.. r.Scores.Select(s => RadarChart.SeriesKeys[_picked.IndexOf(s.Key) % RadarChart.SeriesKeys.Length])], BenchmarkText), legend }
        };
        return Section("data_pie", "Benchmarks", body);
    }

    private static Control Hint(string text, Control? button, Control? extra = null)
    {
        var sp = new StackPanel
        {
            Spacing = Space.L,
            Children = { Txt(text, TextSize.Small, false, Muted) }
        };
        if (button != null)
        {
            sp.Children.Add(Row(button, extra ?? new Control()));
        }

        return sp;
    }

    /// <summary>Downloads the benchmarks in the background when they are due (turned on, a key, a day since the last): the page shows them when it is redrawn.</summary>
    private void RefreshBenchmarksWhenDue()
    {
        if (BenchmarksSection.Due(x))
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await BenchmarksSection.Fetch(x);
                x.Refresh();
            });
        }
    }

    private static string BenchmarkText(int axis, double? v)
    {
        if (v == null)
        {
            return "unknown";
        }

        var n = v.Value;
        return BenchmarkCompare.Axes[axis].Field switch
        {
            "speed" => n.ToString("0.#", CultureInfo.InvariantCulture) + " tokens/s",
            "firstToken" => n.ToString("0.##", CultureInfo.InvariantCulture) + " s",
            "costPerTask" => "$" + n.ToString("0.00##", CultureInfo.InvariantCulture) + " per task",
            _ => n.ToString("0.#", CultureInfo.InvariantCulture)
        };
    }
}
