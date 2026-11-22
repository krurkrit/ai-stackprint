// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;

namespace AiStackprint.Ui;

internal partial class LogViewer
{
    private int _loadId;

    private sealed record FileRead(Session? Parsed, string Raw, long FirstLine, long Bytes, bool Cut, string? Problem)
    {
        /// <summary>A session that is read on in pieces when the file grows (Follow): only what is written after is parsed.</summary>
        public SessionTail? Tail { get; init; }

        /// <summary>The part of the log read (a window that moves as you scroll); null for a format read whole or cut to its end, or a part around sub-agents.</summary>
        public LogSpan? Span { get; init; }

        /// <summary>The last line of the open file in the part read; long.MaxValue when the part reaches its end.</summary>
        public long LastLine { get; init; } = long.MaxValue;
    }

    private long _lastLine = long.MaxValue;   // the last line of the open file in the part shown

    /// <summary>The last line of <paramref name="path"/> in a window that ends before the end of the log.</summary>
    private static long LastLineOf(LogSpan s, string path)
    {
        if (s.AtEnd)
        {
            return long.MaxValue;
        }

        var part = s.Parts.ToList().FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        var end = s.PartOf(Math.Max(0, s.To - 1));
        return part < end ? long.MaxValue : part > end ? 0 : LineIndex.LinesBefore(path, s.To - s.PartStart(part));
    }

    /// <summary>What to read: the window of the log (null: its end), the place to show around, the sub-agent window, and the files of the log (one, or a text log's parts).</summary>
    private sealed record ReadAsk(bool Steps, Func<LogSpan>? Want, DateTime? AgentsBefore, long AtLine, string? Agent, IReadOnlyList<string> Parts);

    /// <summary>
    /// Opens a file: the loading icon shows in the viewer while the file is read and parsed off the UI thread, then the text fades in. A newer call (another file,
    /// F5) makes an older one that is still reading stop. Changes of the open file while it is followed use <see cref="ShowNew"/>, which reads only what is new.
    /// Moving the window (<see cref="Move"/>) keeps the view on screen until the new part is ready, and keeps the step at the top where it was.
    /// </summary>
    private void LoadAsync(bool toEnd) => Fire.Forget(LoadAsyncCore(toEnd));

    private async Task LoadAsyncCore(bool toEnd)
    {
        if (_file is not { } f)
        {
            return;
        }

        _newer.IsVisible = false;
        var id = ++_loadId;
        var anchor = _anchor;
        var rawTop = _rawTop;
        var want = _want;
        _anchor = null;
        _rawTop = null;
        _want = null;
        var moving = anchor != null || rawTop != null;
        if (!moving)
        {
            _view.Content = Busy.Loading("Reading the log…");
        }

        var ask = new ReadAsk(ShowReadable, want, _agentsFrom, _aroundLine, _aroundAgent, PartsOf(f));
        var r = await Task.Run(() => ReadFile(f, ask), CancellationToken.None);
        if (id != _loadId || _file != f)
        {
            return;   // another file was opened, or the page was left, while this one was read
        }

        _readSize = r.Bytes;
        _tail = r.Tail;
        _span = r.Span;
        _lastLine = r.LastLine;
        _rawAsked = false;
        _window = r.Span is { AtEnd: false };
        _linesOk = r.Span != null || !r.Cut;
        ReadMarks();
        var window = (ask.AgentsBefore != null || ask.AtLine > 0 || ask.Agent != null) && r.Parsed != null && !r.Cut && r.Span == null;   // ReadAround returns a whole-file read; a null from it falls back to the cut end
        _status.Text = r.Problem ?? (r.Span is { Cut: true } s ? Where(s) : r.Cut ? $"Showing the last {(ask.Steps ? "8 MB" : "256 KB")}" : window ? (ask.AtLine > 0 ? "Showing the part of the file around the result" : ask.Agent != null ? "Showing the part of the session around the sub-agent" : "Showing the part of the session around sub-agents") : "");
        if (r.Problem != null)
        {
            _raw = "";
            _readable = null;
        }
        else if (r.Parsed != null)
        {
            _agentsWindow = window;
            _readable = new ReadableLog(r.Parsed, toEnd && !window && anchor == null, Compact && !IsLines, _marks, NavFor(r.Parsed), PagingFor(r.Span), anchor);
            if (window && ask.AtLine == 0 && ask.Agent == null && r.Parsed.Entries.FirstOrDefault(e => e.SubAgent) is { } first)
            {
                _readable.Reveal(first);
            }
        }
        else
        {
            _raw = r.Raw;
            _firstLine = r.FirstLine;
        }

        Render(toEnd && rawTop == null);
        if (rawTop is { } top)
        {
            KeepRawTop(top);
        }
        else if (!moving && _view.Content is Control shown)
        {
            Reveal.Fade(shown);
        }

        GoToPending();
        ShowRememberedPlace();
        var keep = _keepResults || AllFiles;   // a result was clicked (the file, or the part of it, loads to show it), or the results are of every file: they stay as they are, not searched again
        _keepResults = false;
        if (!keep && _query.Text is { Length: > 0 })
        {
            Search();
        }
    }

    private static FileRead ReadFile(LogFile f, ReadAsk a)
    {
        try
        {
            var size = new FileInfo(f.Path).Length;
            var truncated = size > (a.Steps ? ReadableBytes : RawBytes);
            if (a.Steps && (a.Agent != null ? SessionAgents.ReadAroundAgent(f, a.Agent, ReadableLog.MaxSteps) : a.AgentsBefore is { } before ? SessionAgents.ReadAround(f, before, ReadableLog.MaxSteps) : null) is { } agents)
            {
                return new FileRead(agents, "", 1, size, false, null);   // read whole: the steps have the file's own line numbers
            }

            if (!a.Steps)
            {
                var raw = a.Want?.Invoke() ?? (a.AtLine > 0 ? LogWindow.Around([f.Path], 0, a.AtLine, RawBytes) : LogWindow.End([f.Path], RawBytes));
                var (text, first) = LogWindow.Text(raw);
                return new FileRead(null, text, first, size, raw.Cut, null) { Span = raw, LastLine = LastLineOf(raw, f.Path) };
            }

            if (!LogWindow.Fits(f.Format))
            {
                return new FileRead(SessionAgents.Read(f, ReadableBytes), "", 1, size, truncated, null);   // a format read whole (or its end): it cannot be read in parts
            }

            var part = Math.Max(0, a.Parts.ToList().FindIndex(p => string.Equals(p, f.Path, StringComparison.OrdinalIgnoreCase)));
            var span = a.Want?.Invoke() ?? (a.AtLine > 0 ? LogWindow.Around(a.Parts, part, a.AtLine, LogWindow.Bytes) : null);
            if ((span == null || span.AtEnd) && a.Parts.Count == 1 && !LineLogs.Is(f.Format) && SessionTail.Open(f, LogWindow.Bytes) is { } tail)
            {
                return new FileRead(tail.Session, "", 1, size, tail.Cut, null) { Tail = tail, Span = tail.Span };   // the end: read on as the file grows
            }

            span ??= LogWindow.End(a.Parts, LogWindow.Bytes);
            return new FileRead(LogWindow.Parse(f, span), "", 1, size, span.Cut, null) { Span = span, LastLine = LastLineOf(span, f.Path) };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FileRead(null, "", 1, 0, false, "Cannot read: " + e.Message);
        }
    }
}
