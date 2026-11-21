// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Summary of one app's logs for 7 days / 30 days / all. Session totals are computed in the background.</summary>
internal class LogSummaryCard
{
    private readonly ContentControl _body = new();
    /// <summary>Range shown: 7, 30 or 0 (all). Kept when the Logs page replaces the card.</summary>
    public int Days { get; set; } = 7;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<LogFile> _files = [];

    public Control Build(IReadOnlyList<LogFile> files)
    {
        _files = files;
        var range = new ContentControl();
        void Pills() => range.Content = W.Pills([("7 days", "7"), ("30 days", "30"), ("All", "0")], Days.ToString(), v =>
        {
            Days = int.Parse(v);
            Pills();
            Refresh();
        });
        Pills();
        Refresh();
        return Section("data_pie", "Summary", _body.Free(), range);
    }

    /// <summary>Leaving the page: stop any calculation and drop the shown result.</summary>
    public void Release()
    {
        _cts?.Cancel();
        _cts = null;
        _body.Content = null;
        _files = [];
        _result = false;
    }

    private bool _result;   // a result is on screen

    /// <summary>Recomputes, e.g. after the watcher saw new files.</summary>
    public void Update(IReadOnlyList<LogFile> files)
    {
        _files = files;
        Refresh();
    }

    private void Refresh() => Fire.Forget(RefreshAsync());

    private async Task RefreshAsync()
    {
        _cts?.Cancel();
#pragma warning disable S2930 // disposed in the finally below when done or replaced
        var cts = _cts = new CancellationTokenSource();
#pragma warning restore S2930
        _body.Content ??= Busy.Loading("Calculating the summary…");   // a refresh keeps the last result on screen

        var files = _files;
        var since = Days == 0 ? (DateTime?)null : DateTime.Now.AddDays(-Days);
        try
        {
            var s = await Task.Run(() => LogSummary.Compute(files, since, cts.Token), cts.Token);
            if (!cts.IsCancellationRequested)
            {
                var shown = Show(s);
                _body.Content = _result ? shown : Reveal.Fade(shown);   // the first result fades in over the loading icon; later ones replace it in place
                _result = true;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_cts == cts)
            {
                _cts = null;
            }

            cts.Dispose();   // finished or replaced: a newer refresh has its own
        }
    }

    private static StackPanel Show(LogStats s)
    {
        var w = new WrapPanel();
        void Add(string t, string? key = null) => w.Children.Add(Chip(t, key).With(c => c.Margin = new Thickness(0, 0, Space.M, Space.M)));
        Add(ReadableLog.Plural(s.Files, "file") + " · " + LogViewer.Size(s.Bytes));
        if (s.Newest is { } n)
        {
            Add($"Last activity {n:yyyy-MM-dd HH:mm}");
        }

        if (s.Oldest is { } o && s.Files > 1)
        {
            Add($"Oldest {o:yyyy-MM-dd}");
        }

        if (s.Errors + s.Warnings > 0 || s.Files > s.Sessions)
        {
            Add(ReadableLog.Plural(s.Errors, "error line"), s.Errors > 0 ? ErrorKey : null);
            Add(ReadableLog.Plural(s.Warnings, "warning line"), s.Warnings > 0 ? WarningKey : null);
        }
        if (s.TextCapped)
        {
            Add("Error count covers the newest 50 MB of text logs");
        }

        if (s.Sessions > 0)
        {
            Add(ReadableLog.Plural(s.Sessions, "session"));
            Add(ReadableLog.Plural(s.Prompts, "prompt"));
            Add(ReadableLog.Plural(s.ToolCalls, "tool call"));
            if (s.SubAgentSteps > 0)
            {
                Add(ReadableLog.Plural(s.SubAgentSteps, "sub-agent step"));
            }

            if (s.InputTokens + s.OutputTokens > 0)
            {
                Add($"{s.InputTokens:N0} in · {s.OutputTokens:N0} out tokens");
            }

            if (s.Cost > 0)
            {
                Add("≈ " + ReadableLog.StepMoney(s.Cost) + " estimated");
            }

            if (s.Models.Count > 0)
            {
                Add(string.Join(", ", s.Models));
            }
        }
        var sp = new StackPanel
        {
            Spacing = Space.S,
            Children = { w }
        };
        if (s.TopTools.Count > 0)
        {
            sp.Children.Add(Txt("Top tools: " + string.Join(" · ", s.TopTools.Select(t => $"{t.Tool} {t.Count:N0}")), TextSize.Small, false, Muted));
        }

        if (s.Daily.Count > 0)
        {
            sp.Children.Add(PerDay(s.Daily));
        }

        if (s.Largest.Count > 0)
        {
            sp.Children.Add(Txt("Largest: " + string.Join(" · ", s.Largest.Select(f => $"{Path.GetFileName(f.Path)} {LogViewer.Size(f.Size)}")), TextSize.Small, false, Muted));
        }

        if (s.Files == 0)
        {
            sp.Children.Add(Txt("No log files in this period.", TextSize.Small, false, Muted));
        }

        return sp;
    }

    private const int DaysShown = 14;

    /// <summary>Tokens and estimated cost per day (newest first): the sessions that had a reply that day, the request and reply tokens, and the cost at the models' list prices.</summary>
    private static Control PerDay(List<DayTotal> days)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Widths.LabelS},{Widths.LabelS},*,{Widths.LabelS}"),
            Margin = new Thickness(0, Space.S, 0, 0)
        };
        void Cell(int row, int col, string text, bool head = false, bool right = false)
        {
            var t = Txt(text, TextSize.Small, head, head ? Muted : Text);
            if (right)
            {
                t.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            }

            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }

        var shown = days.Take(DaysShown).ToList();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell(0, 0, "Day", true);
        Cell(0, 1, "Sessions", true);
        Cell(0, 2, "Tokens in / out", true);
        Cell(0, 3, "Est. cost", true, true);
        for (var i = 0; i < shown.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var d = shown[i];
            Cell(i + 1, 0, d.Date.ToString("yyyy-MM-dd"));
            Cell(i + 1, 1, d.Sessions.ToString("N0"));
            Cell(i + 1, 2, $"{UsageCard.Fmt(d.Input)} / {UsageCard.Fmt(d.Output)}");
            Cell(i + 1, 3, d.Cost > 0 ? ReadableLog.StepMoney(d.Cost) : "–", false, true);
        }

        var panel = new StackPanel { Spacing = Space.XS, Children = { grid } };
        if (days.Count > shown.Count)
        {
            panel.Children.Add(Txt($"… and {days.Count - shown.Count:N0} earlier days", TextSize.Caption, false, Muted));
        }

        return panel;
    }
}
