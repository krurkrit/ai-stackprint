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
        var r = await Task.Run(() => ReadFile(f, readable), CancellationToken.None);
        if (id != _loadId || _file != f)
        {
            return;   // another file was opened, or the page was left, while this one was read
        }

        _readSize = r.Bytes;
        _status.Text = r.Problem ?? (r.Cut ? $"Showing the last {(readable ? "8 MB" : "256 KB")}" : "");
        if (r.Problem != null)
        {
            _raw = "";
            _readable = null;
        }
        else if (r.Parsed != null) { _readable = new ReadableLog(r.Parsed, toEnd); }
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

        if (_query.Text is { Length: > 0 })
        {
            Search();
        }
    }

    private static FileRead ReadFile(LogFile f, bool readable)
    {
        try
        {
            var size = new FileInfo(f.Path).Length;
            var truncated = size > (readable ? ReadableBytes : RawBytes);
            if (readable)
            {
                return new FileRead(LogParsers.Find(f.Format)!.Parse(LogReader.Utf8Lines(f.Path, ReadableBytes)), "", 1, size, truncated, null);
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
