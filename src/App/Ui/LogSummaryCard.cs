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
    private IReadOnlyList<LogFile> _files = Array.Empty<LogFile>();

    public Control Build(IReadOnlyList<LogFile> files)
    {
        _files = files;
        var range = new ContentControl();
        void Pills() => range.Content = W.Pills([("7 days", "7"), ("30 days", "30"), ("All", "0")], Days.ToString(), v => { Days = int.Parse(v); Pills(); Refresh(); });
        Pills();
        Refresh();
        return Section("data_pie", "Summary", _body.Free(), range);
    }

    /// <summary>Leaving the page: stop any calculation and drop the shown result.</summary>
    public void Release() { _cts?.Cancel(); _cts = null; _body.Content = null; _files = Array.Empty<LogFile>(); }

    /// <summary>Recomputes, e.g. after the watcher saw new files.</summary>
    public void Update(IReadOnlyList<LogFile> files) { _files = files; Refresh(); }

    private async void Refresh()
    {
        _cts?.Cancel();
#pragma warning disable S2930 // disposed in the finally below when done or replaced
        var cts = _cts = new CancellationTokenSource();
#pragma warning restore S2930
        if (_body.Content == null)
        {
            _body.Content = Txt("Calculating…", 13, false, Muted);
        }

        var files = _files; var since = Days == 0 ? (DateTime?)null : DateTime.Now.AddDays(-Days);
        try
        {
            var s = await Task.Run(() => LogSummary.Compute(files, since, cts.Token), cts.Token);
            if (!cts.IsCancellationRequested)
            {
                _body.Content = Show(s);
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
        void Add(string t, string? key = null) => w.Children.Add(Chip(t, key).With(c => c.Margin = new Thickness(0, 0, 8, 8)));
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
        { Add(ReadableLog.Plural(s.Errors, "error line"), s.Errors > 0 ? ErrorKey : null); Add(ReadableLog.Plural(s.Warnings, "warning line"), s.Warnings > 0 ? WarningKey : null); }
        if (s.TextCapped)
        {
            Add("Error count covers the newest 50 MB of text logs");
        }

        if (s.Sessions > 0)
        {
            Add(ReadableLog.Plural(s.Sessions, "session")); Add(ReadableLog.Plural(s.Prompts, "prompt")); Add(ReadableLog.Plural(s.ToolCalls, "tool call"));
            if (s.SubAgentSteps > 0)
            {
                Add(ReadableLog.Plural(s.SubAgentSteps, "sub-agent step"));
            }

            if (s.InputTokens + s.OutputTokens > 0)
            {
                Add($"{s.InputTokens:N0} in · {s.OutputTokens:N0} out tokens");
            }

            if (s.Models.Count > 0)
            {
                Add(string.Join(", ", s.Models));
            }
        }
        var sp = new StackPanel { Spacing = 6, Children = { w } };
        if (s.TopTools.Count > 0)
        {
            sp.Children.Add(Txt("Top tools: " + string.Join(" · ", s.TopTools.Select(t => $"{t.Tool} {t.Count:N0}")), 13, false, Muted));
        }

        if (s.Largest.Count > 0)
        {
            sp.Children.Add(Txt("Largest: " + string.Join(" · ", s.Largest.Select(f => $"{Path.GetFileName(f.Path)} {LogViewer.Size(f.Size)}")), 13, false, Muted));
        }

        if (s.Files == 0)
        {
            sp.Children.Add(Txt("No log files in this period.", 13, false, Muted));
        }

        return sp;
    }
}
