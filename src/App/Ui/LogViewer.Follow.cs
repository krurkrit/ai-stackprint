// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>
/// The open file grew (Follow, or "New lines"): what is new is read off the UI thread. A session reads and parses only the bytes written since (its
/// <see cref="SessionTail"/>) and its new steps are added at the end of the view; other formats read the file's end again. The search results
/// are not searched again: the matches in what was added are put at their end.
/// </summary>
internal partial class LogViewer
{
    private SessionTail? _tail;   // the open session, read on in pieces (null: not a session, a window of it, or the raw view)
    private bool _updating, _updateAgain;   // one update at a time; changes during one make one more

    private void ShowNew() => Fire.Forget(ShowNewAsync());

    private async Task ShowNewAsync()
    {
        if (_updating)
        {
            _updateAgain = true;
            return;
        }

        _updating = true;
        try
        {
            do
            {
                _updateAgain = false;
                await UpdateOnce();
            }
            while (_updateAgain && _file != null);
        }
        finally
        {
            _updating = false;
        }
    }

    private async Task UpdateOnce()
    {
        _newer.IsVisible = false;
        if (_file is not { } f)
        {
            return;
        }

        if (_window || _agentsWindow)
        {
            _newer.IsVisible = true;   // an older part of the log is shown: "New lines" goes back to its end
            return;
        }

        if (ShowReadable && _tail is { } tail && _readable != null)
        {
            await NextSteps(f, tail);
        }
        else
        {
            await ReadEnd(f);
        }
    }

    /// <summary>A session: parses only what was written since, adds the new steps at the end (or builds the view afresh when the file was read again).</summary>
    private async Task NextSteps(Models.LogFile f, SessionTail tail)
    {
        var id = _loadId;
        SessionTail.Update u;
        long size;
        try
        {
            (u, size) = await Task.Run(() => (tail.Next(), new FileInfo(f.Path).Length), CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = "Cannot read: " + e.Message;
            return;
        }

        if (id != _loadId || _file != f || _tail != tail || _readable is not { } log)
        {
            return;   // another file was opened, or the file was read again, meanwhile
        }

        _readSize = size;
        _span = tail.Span;
        _status.Text = tail.Cut ? Where(tail.Span) : "";
        if (!u.Rebuilt && log.Append(u.Session, u.Added))
        {
            log.Flash(u.Added, FadeSeconds);
            log.ScrollToEnd();
            AddStepHits(u.Added);
            return;
        }

        var last = log.Steps.LastOrDefault();
        _linesOk = true;
        ReadMarks();
        _readable = new ReadableLog(u.Session, true, Compact && !IsLines, _marks, NavFor(u.Session), PagingFor(tail.Span));
        Render(true);
        _readable.Flash(_readable.After(last), FadeSeconds);
        if (u.Rebuilt)
        {
            SearchAgain();   // read from the start again: the results may have changed anywhere
        }
        else
        {
            AddStepHits(u.Added);
        }
    }

    /// <summary>A text log, a format read whole, or Raw: reads the file's end again in the background, as when it opens, and tints what is new.</summary>
    private async Task ReadEnd(Models.LogFile f)
    {
        var id = _loadId;
        var readable = ShowReadable;
        var before = _readSize;
        var lastStep = readable ? _readable?.Steps.LastOrDefault() : null;
        var ask = new ReadAsk(readable, null, null, 0, null, PartsOf(f));
        var r = await Task.Run(() => ReadFile(f, ask), CancellationToken.None);
        if (id != _loadId || _file != f)
        {
            return;
        }

        _status.Text = r.Problem ?? (r.Span is { Cut: true } s ? Where(s) : r.Cut ? $"Showing the last {(readable ? "8 MB" : "256 KB")}" : "");
        _readSize = r.Bytes;
        _span = r.Span;
        _lastLine = r.LastLine;
        var newLines = 0;
        if (r.Problem != null)
        {
            _raw = "";
            _readable = null;
        }
        else if (r.Parsed != null)
        {
            _tail = r.Tail;
            _linesOk = r.Span != null || !r.Cut;
            ReadMarks();
            _readable = new ReadableLog(r.Parsed, true, Compact && !IsLines, _marks, NavFor(r.Parsed), PagingFor(r.Span));
        }
        else
        {
            _raw = r.Raw;
            _firstLine = r.FirstLine;
            newLines = r.Bytes > before && before > 0 ? NewLines(r.Raw, r.Bytes - before) : 0;
        }

        Render(true);
        if (_readable != null && readable)
        {
            _readable.Flash(_readable.After(lastStep), FadeSeconds);
            SearchAgain();   // the steps were parsed again: searching them is quick (a long session is searched in its file)
        }
        else if (newLines > 0 && _code != null)
        {
            var shown = _raw.Count(c => c == '\n');
            _code.Flash(shown - newLines + 1, FadeSeconds);
            AddLineHits(f, before, _firstLine + shown - newLines);
        }
    }

    /// <summary>Whole lines in the last <paramref name="grewBy"/> bytes of the shown text.</summary>
    private static int NewLines(string text, long grewBy)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        int from = (int)Math.Max(0, bytes.Length - grewBy), n = 0;
        for (var i = from; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>Results of this file that are steps: the new steps that match go at their end (the count grows). Nothing while a search runs or for results of every file.</summary>
    private void AddStepHits(IReadOnlyList<LogEntry> added)
    {
        if (!CanAddHits(true) || Pattern() is not { } p)
        {
            return;
        }

        var steps = added.Where(p.IsMatch).ToList();
        foreach (var e in steps.Where(_ => _jumps.Count < ResultCap))
        {
            var where = (e.Source != null ? "Sub-agent · " : "") + $"{e.Time:HH:mm:ss} {e.Role}{(e.Tool is { } t ? " · " + t : "")}";
            _jumps.Add(() => RevealStep(e));
            var at = _jumps.Count - 1;
            AddRow(() => Hit(where, Around($"{e.Text} {e.Detail}", p), p, at));
        }

        Counted(steps.Count);
    }

    /// <summary>Raw results: searches only the bytes written since the last read (from <paramref name="from"/>, whose line is <paramref name="firstLine"/>) and adds the matches.</summary>
    private void AddLineHits(Models.LogFile f, long from, long firstLine)
    {
        if (!CanAddHits(false) || Pattern() is not { } p)
        {
            return;
        }

        var id = _loadId;
        Fire.Forget(Add());
        async Task Add()
        {
            List<SearchHit> hits;
            try
            {
                hits = await Task.Run(() => LogSearch.File(f.Path, p, Math.Max(0, LogSearch.MaxHits - _hits.Count), CancellationToken.None, from, firstLine), CancellationToken.None);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;
            }

            if (id != _loadId || _file != f || !CanAddHits(false))
            {
                return;
            }

            foreach (var h in hits)
            {
                _hits.Add(h);
                _jumps.Add(() => Jump(h));
                var at = _jumps.Count - 1;
                AddRow(() => Hit($"Line {h.Line:N0}", h.Text, p, at));
            }

            Counted(hits.Count);
        }
    }

    private bool CanAddHits(bool steps) => _rows != null && _results.Content != null && _search == null && !AllFiles && _stepResults == steps && _query.Text is { Length: > 0 };

    /// <summary>Adds to the count line: "N steps", or "N matches" (with "of" in front once Enter or a click went to one).</summary>
    private void Counted(int more)
    {
        if (more == 0 || _count == null)
        {
            return;
        }

        _found += more;
        var total = _stepResults
            ? _found > _jumps.Count ? $"{ReadableLog.Plural(_found, "step")} (first {_jumps.Count:N0} listed)" : ReadableLog.Plural(_found, "step")
            : _found >= LogSearch.MaxHits ? $"first {LogSearch.MaxHits:N0} matches" : $"{_found:N0} {(_found == 1 ? "match" : "matches")}";
        _count.Tag = total;
        _count.Text = _at >= 0 ? $"{_at + 1:N0} of {total}" : total;
    }

    /// <summary>Searches again when results are shown for this file (not every file's: a followed file does not change those).</summary>
    private void SearchAgain()
    {
        if (_query.Text is { Length: > 0 } && !AllFiles)
        {
            Dispatcher.UIThread.Post(Search, DispatcherPriority.Background);
        }
    }
}
