// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;

namespace AiStackprint.Ui;

internal partial class LogViewer
{
    private int _loadId;

    private sealed record FileRead(Session? Parsed, string Raw, long FirstLine, long Bytes, bool Cut, string? Problem);

    /// <summary>
    /// Opens a file: the loading icon shows in the viewer while the file is read and parsed off the UI thread, then the text fades in. A newer call (another file,
    /// F5) makes an older one that is still reading stop. Changes of the open file while it is followed use <see cref="Load"/>, which keeps the scroll position.
    /// </summary>
    private void LoadAsync(bool toEnd) => Fire.Forget(LoadAsyncCore(toEnd));

    private async Task LoadAsyncCore(bool toEnd)
    {
        if (_file is not { } f)
        {
            return;
        }

        _newer.IsVisible = false;
        if (_window && !Follow)
        {
            Render(false);
            return;
        }

        var id = ++_loadId;
        var readable = ShowReadable;
        _view.Content = Busy.Loading("Reading the log…");
        var around = _agentsFrom;
        var atLine = _aroundLine;
        var agent = _aroundAgent;
        var r = await Task.Run(() => ReadFile(f, readable, around, atLine, agent), CancellationToken.None);
        if (id != _loadId || _file != f)
        {
            return;   // another file was opened, or the page was left, while this one was read
        }

        _readSize = r.Bytes;
        _linesOk = !r.Cut;
        ReadMarks();   // a file cut to its end: the steps' line numbers do not count from its first line
        var window = (around != null || atLine > 0 || agent != null) && r.Parsed != null && !r.Cut;   // ReadAround returns a whole-file read; a null from it falls back to the cut end
        _status.Text = r.Problem ?? (r.Cut ? $"Showing the last {(readable ? "8 MB" : "256 KB")}" : window ? (atLine > 0 ? "Showing the part of the file around the result" : agent != null ? "Showing the part of the session around the sub-agent" : "Showing the part of the session around sub-agents") : "");
        if (r.Problem != null)
        {
            _raw = "";
            _readable = null;
        }
        else if (r.Parsed != null)
        {
            _agentsWindow = window;
            _readable = new ReadableLog(r.Parsed, toEnd && !window, Compact && !IsLines, _marks, NavFor(r.Parsed));
            if (window && atLine == 0 && agent == null && r.Parsed.Entries.FirstOrDefault(e => e.SubAgent) is { } first)
            {
                _readable.Reveal(first);
            }
        }
        else
        {
            _raw = r.Raw;
            _firstLine = r.FirstLine;
        }
        Render(toEnd);
        if (_view.Content is Control shown)
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

    private static FileRead ReadFile(LogFile f, bool readable, DateTime? around, long atLine, string? agent)
    {
        try
        {
            var size = new FileInfo(f.Path).Length;
            var truncated = size > (readable ? ReadableBytes : RawBytes);
            if (readable && (agent != null ? SessionAgents.ReadAroundAgent(f, agent, ReadableLog.MaxSteps) : atLine > 0 ? SessionAgents.ReadAroundLine(f, atLine, ReadableLog.MaxSteps) : around is { } before ? SessionAgents.ReadAround(f, before, ReadableLog.MaxSteps) : null) is { } window)
            {
                return new FileRead(window, "", 1, size, false, null);   // read whole: the steps have the file's own line numbers
            }

            if (readable)
            {
                return new FileRead(LineLogs.Is(f.Format) ? LineLogs.Parse(LogReader.Utf8Lines(f.Path, ReadableBytes)) : SessionAgents.Read(f, ReadableBytes), "", 1, size, truncated, null);
            }

            var text = LogReader.Tail(f.Path, RawBytes).Text;
            return new FileRead(null, text, truncated ? LogSearch.LinesBefore(f.Path, size - System.Text.Encoding.UTF8.GetByteCount(text)) + 1 : 1, size, truncated, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FileRead(null, "", 1, 0, false, "Cannot read: " + e.Message);
        }
    }
}
