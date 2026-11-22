// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>
/// A long log is shown a window at a time (<see cref="LogWindow"/>): 5 MB of steps, 256 KB of raw text. Scrolling into the first or last quarter of it reads
/// the window a quarter further on, which replaces it with the step (or line) at the top kept where it was. A text log split into parts is read across them.
/// </summary>
internal partial class LogViewer
{
    private LogSpan? _span;         // the part of the log shown; null: a format read whole, or a part around sub-agents
    private Func<LogSpan>? _want;   // the window to read on the next load (it reads the file, so it runs off the UI thread)
    private Anchor? _anchor;        // the step to keep at its height on the next load
    private long? _rawTop;          // the file line to keep at the top of the raw view on the next load
    private bool _rawAsked;         // the raw view has asked for the next window

    /// <summary>The files read as one log: a text log's parts in Lines, else only the file.</summary>
    private IReadOnlyList<string> PartsOf(LogFile f) => ShowReadable && LineLogs.Is(f.Format) && Files?.Invoke() is { } files
        ? [.. LogParts.Of(f, files).Select(p => p.Path)]
        : [f.Path];

    /// <summary>The header's note on the part shown: where it is in the log, and how many files the log has.</summary>
    private static string Where(LogSpan s) => "Showing " + LogWindow.Where(s) + (s.Parts.Count > 1 ? $" ({s.Parts.Count} files)" : "") + " · scroll to read on";

    /// <summary>What the steps view may ask for: the part before and after this one; null for a format read whole.</summary>
    private Paging? PagingFor(LogSpan? s) => s == null ? null : new Paging(s.AtStart ? null : () => Move(-1), s.AtEnd ? null : () => Move(1), _file?.Path);

    /// <summary>Reads the window a quarter toward the start (-1) or the end (1); the view stays until it is ready.</summary>
    private void Move(int direction)
    {
        if (_span is not { } s || _file == null)
        {
            return;
        }

        if (ShowReadable)
        {
            _anchor = _readable?.TopAnchor() ?? new Anchor(null, -1, 0, 0);   // nothing built: no step to keep in place
        }
        else
        {
            _rawTop = _firstLine + Math.Max(1, _code?.LineAt(0) ?? 1) - 1;
        }

        var bytes = ShowReadable ? LogWindow.Bytes : RawBytes;
        _want = direction < 0 ? () => LogWindow.Earlier(s, bytes) : () => LogWindow.Later(s, bytes);
        _keepResults = true;   // the results are of the whole file: moving the window does not change them
        _status.Text = direction < 0 ? "Reading the earlier part…" : "Reading the later part…";
        LoadAsync(false);
    }

    /// <summary>The raw view scrolled into the first or last quarter of its lines: the window moves (not while it shows only matching lines).</summary>
    private void RawScrolled(double delta)
    {
        if (ShowReadable || _rawAsked || _span is not { } s || _code is not { } code || _only.IsChecked == true || code.LineCount < 2)
        {
            return;
        }

        var lines = code.LineCount;
        if ((delta < 0 && !s.AtStart && code.LineAt(0) < lines / 4) || (delta > 0 && !s.AtEnd && code.LineAt(1) > lines - lines / 4))
        {
            _rawAsked = true;
            Move(delta < 0 ? -1 : 1);
        }
    }

    /// <summary>After the raw window moved: puts the line that was at the top back at the top.</summary>
    private void KeepRawTop(long fileLine)
    {
        _rawAsked = false;
        var line = (int)Math.Max(1, fileLine - _firstLine + 1);
        Dispatcher.UIThread.Post(() => _code?.ScrollLineToTop(line), DispatcherPriority.Loaded);
    }

    /// <summary>"New lines" or Follow while an older part is shown: back to the end of the log.</summary>
    private void ShowLatest()
    {
        _span = null;
        _want = null;
        _window = false;
        _aroundLine = 0;
        LoadAsync(true);
    }
}
