// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia.Controls;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class LogViewer
{
    private LogQuery? Pattern()
    {
        try { return LogQuery.Parse(_query.Text, _case.IsChecked == true, _regex.IsChecked == true, steps: ShowReadable); }
        catch (ArgumentException) { return null; }
    }

    private readonly List<SearchHit> _hits = [];
    private readonly List<Action> _jumps = [];   // one per result row, for Enter / Shift+Enter
    private TextBlock? _count;
    private StackPanel? _rows;     // the result rows shown: the matches in what a followed file adds go at their end
    private int _found;            // results the count says (more than the rows when they are capped)
    private bool _stepResults;     // the results are steps (Conversation, Compact), not lines
    private int _at = -1;

    /// <summary>Goes to the next (1) or previous (-1) result, round the end.</summary>
    private void Next(int step)
    {
        if (_jumps.Count == 0)
        {
            return;
        }

        Go(((_at < 0 && step < 0 ? 0 : _at) + step + _jumps.Count) % _jumps.Count);
    }

    private void Go(int i)
    {
        _at = i;
        Jumping(_jumps[i]);
        _count?.Text = $"{i + 1:N0} of {_count.Tag}";
    }

    /// <summary>Shows the loading icon over the log, then goes to the result: building the steps around it can take a moment and the icon must be on screen first.</summary>
    private void Jumping(Action go)
    {
        _jumping.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                go();
            }
            finally
            {
                Dispatcher.UIThread.Post(() => _jumping.IsVisible = false, DispatcherPriority.Background);   // after the view has laid out
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>Raw: searches the whole file in the background. Conversation: searches the steps. Results go in the side panel.</summary>
    private void Search() => Fire.Forget(SearchAsync());

    /// <summary>The --perf run: searches the open file for <paramref name="text"/> (its whole session in Conversation), or every file.</summary>
    internal void SearchForPerf(string text, bool allFiles)
    {
        _query.Text = text;
        _all.IsChecked = allFiles;
        Search();
    }

    private async Task SearchAsync()
    {
        _wait.Stop();
        _search?.Cancel();
        _hits.Clear();
        _jumps.Clear();
        _at = -1;
        _count = null;
        if (AllFiles && !string.IsNullOrEmpty(_query.Text))
        {
            await SearchAllAsync();
            return;
        }

        var p = Pattern();
        if (p == null || _file == null)
        {
            _results.Content = string.IsNullOrEmpty(_query.Text) || _regex.IsChecked != true ? null : Panel(Txt("Invalid pattern.", TextSize.Caption, false, Muted));
            if (_only.IsChecked == true)
            {
                Render(false);
            }

            return;
        }

        if (ShowReadable && !IsLines && !WholeSessionLoaded && LogParsers.Find(_file.Format) != null)
        {
            await SearchSessionAsync(p);
            return;
        }

        if (ShowReadable)
        {
            var steps = _readable?.Steps.Where(p.IsMatch).ToList() ?? [];
            (_found, _stepResults) = (steps.Count, true);
            _jumps.AddRange(steps.Take(500).Select(e => (Action)(() => RevealStep(e))));
            _results.Content = Panel(Count(ReadableLog.Plural(steps.Count, "step")),
                [.. steps.Take(500).Select((e, i) => (Func<Control>)(() => Hit($"{e.Time:HH:mm:ss} {e.Role}{(e.Tool is { } t ? " · " + t : "")}", Around($"{e.Text} {e.Detail}", p), p, i)))]);
            return;
        }
#pragma warning disable S2930 // disposed in the finally below when done or replaced
        var cts = _search = new CancellationTokenSource();
#pragma warning restore S2930
        var path = _file.Path;
        _results.Content = Panel(Busy.Loading("Searching the whole file…", 14));
        try
        {
            var hits = await Task.Run(() => LogSearch.File(path, p, LogSearch.MaxHits, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _hits.AddRange(hits);
            (_found, _stepResults) = (hits.Count, false);
            _jumps.AddRange(hits.Select(h => (Action)(() => Jump(h))));
            _results.Content = Panel(Count(hits.Count >= LogSearch.MaxHits ? $"first {LogSearch.MaxHits:N0} matches" : $"{hits.Count:N0} {(hits.Count == 1 ? "match" : "matches")}"),
                [.. hits.Select((h, i) => (Func<Control>)(() => Hit($"Line {h.Line:N0}", h.Text, p, i)))]);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _results.Content = Panel(Txt("Cannot read: " + e.Message, TextSize.Caption, false, Muted)); }
        finally
        {
            if (_search == cts)
            {
                _search = null;
            }

            cts.Dispose();   // finished or replaced: a newer search has its own
        }
        if (_only.IsChecked == true)
        {
            Render(false);
        }
    }

    /// <summary>Scrolls the raw view to a hit; loads the part of the file around it when it is outside what is shown.</summary>
    private void Jump(SearchHit h)
    {
        if (_code == null || _file == null)
        {
            return;
        }

        var lastShown = _firstLine + _raw.Count(c => c == '\n');
        if (_only.IsChecked == true)
        {
            _only.IsChecked = false;
            Render(false);
        }
        if (h.Line < _firstLine || h.Line > lastShown)
        {
            try
            {
                _span = LogWindow.Around([_file.Path], 0, h.Line, RawBytes);   // the window around the match: scrolling reads on from there
                (_raw, _firstLine) = LogWindow.Text(_span);
                _lastLine = LastLineOf(_span, _file.Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _status.Text = "Cannot read: " + e.Message;
                return;
            }
            _window = !_span.AtEnd;
            Follow = false;
            _status.Text = Where(_span);
            Render(false);
        }
        var line = (int)(h.Line - _firstLine + 1);
        Dispatcher.UIThread.Post(() =>
        {
            _code.Reveal(line, h.Column);
            _code.Flash(line, line, FadeSeconds);   // the line is selected and also tinted, so it shows even when the box has no focus
        }, DispatcherPriority.Background);
    }

    /// <summary>A step's text on one line, cut around its first match.</summary>
    private static string Around(string s, LogQuery q)
    {
        var line = s.ReplaceLineEndings(" ").Trim();
        var first = q.Spans(line) is { Count: > 0 } sp ? sp[0] : (Start: 0, Length: 0);
        return LogSearch.Snippet(line, first.Start, first.Length);
    }

    public static string Size(long b) => b < 1024 ? $"{b} B" : b < 1 << 20 ? $"{b / 1024.0:0.#} KB" : $"{b / 1048576.0:0.#} MB";
}
