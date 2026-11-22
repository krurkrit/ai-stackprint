// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Search across the app's log files (newest first): the results are grouped by file, and a click opens that file in Raw at the line.</summary>
internal partial class LogViewer
{
    private const int MaxFiles = 200, MaxHitsPerFile = 20;
    private readonly ToggleButton _all = new()
    {
        Content = "All files",
        IsChecked = true,
        Padding = new Avalonia.Thickness(Space.M, Space.XS)
    };
    private SearchHit? _pending;   // a line to go to once the file that was opened for a search result has loaded

    /// <summary>The app's log files, newest first (the Files list).</summary>
    public Func<List<LogFile>>? Files { get; set; }

    /// <summary>Opens another file in the viewer.</summary>
    public Action<LogFile>? OpenFile { get; set; }

    private bool Several => Files?.Invoke().Count > 1;
    private bool AllFiles => _all.IsChecked == true && Several;

    private async Task SearchAllAsync()
    {
        LogQuery? p;
        try { p = LogQuery.Parse(_query.Text, _case.IsChecked == true, _regex.IsChecked == true, steps: false); }
        catch (ArgumentException) { p = null; }
        if (p == null)
        {
            _results.Content = _regex.IsChecked == true ? Panel(Txt("Invalid pattern.", TextSize.Caption, false, Muted)) : null;
            return;
        }
#pragma warning disable S2930 // disposed in the finally below when done or replaced
        var cts = _search = new CancellationTokenSource();
#pragma warning restore S2930
        var files = Files!().Where(f => !f.Path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)).Take(MaxFiles).ToList();
        _results.Content = Panel(Busy.Loading("Searching the log files…", 14));
        try
        {
            var found = await Task.Run(() => FindIn(files, p, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var rows = new List<Control>();
            var index = 0;
            foreach (var (f, hits) in found)
            {
                rows.Add(Txt($"{f.Modified:yyyy-MM-dd HH:mm} · {f.Label} · {hits.Count}{(hits.Count >= MaxHitsPerFile ? "+" : "")}", TextSize.Caption, true, Muted)
                    .With(t => t.Margin = new Avalonia.Thickness(Space.XS, Space.M, 0, Space.XXS)));
                foreach (var h in hits)
                {
                    var file = f;
                    var hit = h;
                    _jumps.Add(() => GoTo(file, hit));
                    rows.Add(Hit($"{(h.Source != null ? "Sub-agent · " : "")}Line {h.Line:N0}", h.Text, p, index++));
                }
            }

            var total = found.Sum(g => g.Hits.Count);
            _results.Content = Panel(Count(total == 0 ? "no matches" : $"{total:N0} {(total == 1 ? "match" : "matches")} in {ReadableLog.Plural(found.Count, "file")}"), rows);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_search == cts)
            {
                _search = null;
            }

            cts.Dispose();
        }
    }

    private static List<(LogFile File, List<SearchHit> Hits)> FindIn(List<LogFile> files, LogQuery p, CancellationToken ct)
    {
        var found = new List<(LogFile, List<SearchHit>)>();
        var total = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var hits = LogSearch.File(f.Path, p, Math.Min(MaxHitsPerFile, LogSearch.MaxHits - total), ct);
                if (f.Format == "claude-session")
                {
                    foreach (var agent in SessionAgents.AgentFiles(f.Path))   // the sub-agents' own files: their lines are reached by their file and line
                    {
                        ct.ThrowIfCancellationRequested();
                        if (hits.Count >= MaxHitsPerFile)
                        {
                            break;
                        }

                        hits.AddRange(LogSearch.File(agent, p, MaxHitsPerFile - hits.Count, ct).Select(h => h with { Source = agent }));
                    }
                }

                if (hits.Count > 0)
                {
                    found.Add((f, hits));
                    total += hits.Count;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // a file in use or unreadable: left out
            if (total >= LogSearch.MaxHits)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>A result in some file: it goes to that line in the view you are in (the step that holds it in Conversation and Compact, the row in Lines, the line in Raw).</summary>
    private void GoTo(LogFile f, SearchHit h)
    {
        _pending = h;
        if (f.Path != _file?.Path)
        {
            OpenFile?.Invoke(f);   // GoToPending goes to the line once it has loaded
        }
        else
        {
            GoToPending();
        }
    }

    /// <summary>After a file has loaded (or at once for the open file): goes to the line a search result pointed at.</summary>
    private void GoToPending()
    {
        if (_pending is not { } h)
        {
            return;
        }

        if (h.Source != null && !ShowReadable)
        {
            _pending = null;
            _status.Text = "A sub-agent's steps are shown in Conversation or Compact";
            return;
        }

        if (ShowReadable)
        {
            var covered = _readable?.Index.Covers(h.Source, h.Line) == true;   // some shown step starts at or before the result
            var shown = IsLines || (covered && (h.Source != null || _linesOk));   // and the lines are the file's (not those of a file shown cut to its end)
            if (!shown && h.Source == null && _aroundLine != h.Line)
            {
                ShowLine(h.Line);   // a result the steps shown do not hold: the steps around it are read from the file; this runs again when they are shown
                return;
            }

            if (!shown && h.Source != null && !string.Equals(_aroundAgent, h.Source, StringComparison.OrdinalIgnoreCase))
            {
                ShowAgent(h.Source);   // an agent that is not in the part shown: the part of the session around it is read; this runs again when it is shown
                return;
            }

            _pending = null;
            if (!shown && h.Source != null)
            {
                _status.Text = "That sub-agent step is not in the part shown";
                return;
            }

            RevealAt(h.Source, h.Line, FadeSeconds);
        }
        else if (_code != null)
        {
            _pending = null;
            Jump(h);
        }
    }
}
