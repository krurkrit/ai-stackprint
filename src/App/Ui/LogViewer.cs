// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Read-only viewer for one log file: Raw (highlighted text) or Conversation (sessions), search with a results list and jump-to-line.</summary>
internal partial class LogViewer
{
    private readonly Ctx _x;
    private readonly TextBox _query = SearchBox("Search this file").With(t => t.MinWidth = Widths.Field);
    private readonly DispatcherTimer _wait = new() { Interval = TimeSpan.FromMilliseconds(250) };   // search after a short pause in typing: a raw search reads the whole file
    private readonly ToggleButton _case = new() { Content = "Aa", Padding = new Thickness(Space.M, Space.XS) }, _regex = new() { Content = ".*", Padding = new Thickness(Space.M, Space.XS) }, _only = new() { Content = "Only matching", Padding = new Thickness(Space.M, Space.XS) };
    private readonly ContentControl _view = new(), _results = new();
    private readonly TextBlock _status = Txt("", TextSize.Caption, false, Muted);
    private readonly Button _newer;        // "New lines": the file grew while Follow is off
    private LogFile? _file;
    private CodeView? _code;
    private ReadableLog? _readable;
    private string _raw = "";
    private long _firstLine = 1;           // file line number of the first line in the raw view
    private long _readSize;                // file size when it was last read: what grew after it is highlighted
    private bool _window;                  // raw view shows a window around a search hit instead of the tail
    private CancellationTokenSource? _search;
    public bool Readable { get; set; } = true;
    public bool Follow { get; set; }

    public LogViewer(Ctx ctx)
    {
        _x = ctx;
        ToolTip.SetTip(_case, "Match case"); ToolTip.SetTip(_regex, "Regular expression"); ToolTip.SetTip(_only, "Show only matching lines (Raw)");
        foreach (var t in new[] { _case, _regex, _only })
        {
            t.VerticalContentAlignment = VerticalAlignment.Center;
        }

        ToolTip.SetTip(_query, "Words match separately, in any order, on one line (Raw) or in one step (Conversation); \"quotes\" match a phrase.\nConversation: role:user, role:assistant, tool:bash keep only those steps.\nEnter: next match · Shift+Enter: previous");
        _wait.Tick += (_, _) => { _wait.Stop(); Search(); };
        _query.TextChanged += (_, _) => { _wait.Stop(); _wait.Start(); };
        _case.IsCheckedChanged += (_, _) => Search(); _regex.IsCheckedChanged += (_, _) => Search();
        _only.IsCheckedChanged += (_, _) => Render(false);
        _query.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                Next(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
            }
        };
        _newer = Btn("New lines", () => { _window = false; Load(true, update: true); }, true, "chevron_down")
            .With(b => { b.IsVisible = false; b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, Space.XXL, Space.XL); ToolTip.SetTip(b, "The file grew: show the newest lines"); });
    }

    private bool CanRead => _file != null && LogParsers.Find(_file.Format) != null;
    private bool ShowReadable => Readable && CanRead;

    public Control Build(LogFile? f, Action rebuild)
    {
        if (f?.Path != _file?.Path) { _code?.Dispose(); _code = null; _window = false; _hits.Clear(); }
        _file = f;
        if (f == null)
        {
            return Card(Txt("Select a log file.", TextSize.Body, false, Muted));
        }

        var mode = CanRead ? Btn(ShowReadable ? "Raw" : "Conversation", () => { Readable = !Readable; rebuild(); }, false, ShowReadable ? "code" : "chat_multiple")
            .With(b => ToolTip.SetTip(b, ShowReadable ? "Show the file as written" : "Show the session as a conversation")) : null;
        // Search, the view switch, then the file buttons. Follow is a switch on the Files card. F5 reloads (no Reload button: the folder is watched).
        var tools = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var c in new Control?[] { _query.Free(), _case.Free(), _regex.Free(), ShowReadable ? null : _only.Free(), mode,
                     IconBtn("open", "Open file", () => PathUtil.OpenFile(f.Path)), IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(f.Path)) })
        {
            if (c != null)
            {
                tools.Children.Add(c.With(k => { k.Margin = new Thickness(0, 0, Space.S, Space.S); k.Height = Theme.InputHeight; }));   // one height for every control in the row
            }
        }

        var head = new StackPanel { Spacing = Space.M, Margin = new Thickness(0, 0, 0, Space.S), Children = { Row(Title(Path.GetFileName(f.Path), TextSize.Subtitle), _status.Free().With(s => s.VerticalAlignment = VerticalAlignment.Center)), tools } };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Space.L, Height = Heights.ViewerL };
        _newer.IsVisible = false;
        body.Children.Add(new Border { CornerRadius = new CornerRadius(Radius.M), ClipToBounds = true, Child = new Panel { Children = { _view.Free(), _newer.Free() } } });
        body.Children.Add(_results.Free().With(r => Grid.SetColumn(r, 1)));
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head); dock.Children.Add(body);
        LoadAsync(true);
        return Card(dock);
    }

    private const int ReadableBytes = 8 << 20, RawBytes = 256 * 1024;

    /// <summary>Highlight time for new content, from Settings (0 = off).</summary>
    private double FadeSeconds => Math.Clamp(_x.S.Settings.FadeSeconds, 0, 60);

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
            if (_window && !Follow) { Render(false); return; }
            var size = new FileInfo(f.Path).Length;
            bool readable = ShowReadable, truncated = size > (readable ? ReadableBytes : RawBytes);
            // Size and time are in the file list above; only say when part of the file is left out
            _status.Text = truncated ? $"Showing the last {(readable ? "8 MB" : "256 KB")}" : "";
            if (readable)
            {
                _readable = new ReadableLog(LogParsers.Find(f.Format)!.Parse(LogReader.Utf8Lines(f.Path, ReadableBytes)), toEnd);   // streamed, no copy of the text
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _raw = ""; _readable = null; _status.Text = "Cannot read: " + e.Message; }
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
        _code?.Dispose(); _code = null;
        _readable = null; _raw = ""; _window = false; _file = null; _readSize = 0;
        _view.Content = null; _results.Content = null; _status.Text = ""; _newer.IsVisible = false;
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
        if (ShowReadable) { _view.Content = _readable?.Root; return; }
        _code ??= new CodeView(_file!.Path, lineNumbers: _x.S.Settings.LogLineNumbers) { KeepOnDetach = true };
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
