// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia.Threading;

namespace AiStackprint.Ui;

internal partial class LogViewer
{
    /// <summary>Re-reads the file. Raw: last 256 KB (or the window around a search hit). Conversation: up to 8 MB parsed.</summary>
    /// <param name="update">The file grew while open: highlight what is new.</param>
    public void Load(bool toEnd, bool update = false)
    {
        if (_file is not { } f)
        {
            return;
        }

        _newer.IsVisible = false;
        var lastStep = update ? _readable?.Steps.LastOrDefault() : null;
        var before = update ? _readSize : -1;
        var newLines = 0;
        try
        {
            if (_window && !Follow)
            {
                Render(false);
                return;
            }
            var size = new FileInfo(f.Path).Length;
            bool readable = ShowReadable, truncated = size > (readable ? ReadableBytes : RawBytes);
            // Size and time are in the file list above; only say when part of the file is left out
            _status.Text = truncated ? $"Showing the last {(readable ? "8 MB" : "256 KB")}" : "";
            if (readable)
            {
                _readable = new ReadableLog(LogParsers.Find(f.Format)!.Parse(LogReader.Utf8Lines(f.Path, ReadableBytes)), toEnd, Compact && !IsLines);   // streamed, no copy of the text
            }
            else
            {
                var text = LogReader.Tail(f.Path, RawBytes).Text;
                _raw = text;
                _firstLine = truncated ? LogSearch.LinesBefore(f.Path, size - System.Text.Encoding.UTF8.GetByteCount(text)) + 1 : 1;
                if (before >= 0 && size > before)
                {
                    newLines = NewLines(text, size - before);
                }
            }
            _readSize = size;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _raw = "";
            _readable = null;
            _status.Text = "Cannot read: " + e.Message;
        }
        Render(toEnd);
        if (update && FadeSeconds > 0)
        {
            if (ShowReadable && _readable != null)
            {
                _readable.Flash(_readable.After(lastStep), FadeSeconds);
            }
            else if (newLines > 0 && _code != null)
            {
                _code.Flash(_raw.Count(c => c == '\n') - newLines + 1, FadeSeconds);
            }
        }
        if (_query.Text is { Length: > 0 })
        {
            Search();
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

    /// <summary>F5: read the open file again where it is (the end when following).</summary>
    public void Reload()
    {
        if (!_window)
        {
            LoadAsync(Follow);
        }
    }

    /// <summary>Leaving the Logs page: drops the file text, parsed session, search results, highlighter and controls so the memory can be freed.</summary>
    public void Release()
    {
        _search?.Cancel();
        _hits.Clear();
        _code?.Dispose();
        _code = null;
        _readable = null;
        _raw = "";
        _window = false;
        _file = null;
        _readSize = 0;
        _view.Content = null;
        _results.Content = null;
        _status.Text = "";
        _newer.IsVisible = false;
    }

    /// <summary>The open file changed on disk: reload when following, otherwise offer "New lines" (keeps your scroll position).</summary>
    public void FileChanged()
    {
        if (Follow)
        {
            Load(true, update: true);
        }
        else if (_file != null)
        {
            _newer.IsVisible = true;
        }
    }

    private void Render(bool toEnd)
    {
        if (ShowReadable)
        {
            _view.Content = _readable?.Root;
            return;
        }
        _code ??= new CodeView(_file!.Path, lineNumbers: _x.S.Settings.LogLineNumbers) { KeepOnDetach = true, ScrollPastEnd = true };
        var text = _raw;
        if (_only.IsChecked == true && Pattern() is { } p)
        {
            text = string.Join('\n', text.Split('\n').Where(p.IsMatch));
        }

        _code.Text = text;
        _view.Content = _code.Free();
        if (toEnd)
        {
            Dispatcher.UIThread.Post(_code.ScrollToEnd, DispatcherPriority.Background);
        }
    }
}
