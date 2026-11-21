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
    private readonly ToggleButton _case = new()
    {
        Content = "Aa",
        Padding = new Thickness(Space.M, Space.XS)
    }, _regex = new()
    {
        Content = ".*",
        Padding = new Thickness(Space.M, Space.XS)
    }, _only = new()
    {
        Content = "Only matching",
        Padding = new Thickness(Space.M, Space.XS)
    };
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

    /// <summary>With Readable on: one line per step (a click opens its card) instead of cards.</summary>
    public bool Compact { get; set; }
    public bool Follow { get; set; }

    public LogViewer(Ctx ctx)
    {
        _x = ctx;
        ToolTip.SetTip(_case, "Match case");
        ToolTip.SetTip(_regex, "Regular expression");
        ToolTip.SetTip(_only, "Show only matching lines (Raw)");
        ToolTip.SetTip(_all, "On: search every log file of this app, newest first (results are lines of the files, shown in Raw).\nOff: search only the open file.");
        foreach (var t in new[] { _case, _regex, _only, _all })
        {
            t.VerticalContentAlignment = VerticalAlignment.Center;
        }

        ToolTip.SetTip(_query, "Words match separately, in any order, on one line (Raw) or in one step (Conversation); \"quotes\" match a phrase.\nConversation: role:user, role:assistant, tool:bash keep only those steps. Lines: role:error, role:warn keep only those levels.\nEnter: next match · Shift+Enter: previous");
        _wait.Tick += (_, _) =>
        {
            _wait.Stop();
            Search();
        };
        _query.TextChanged += (_, _) =>
        {
            _wait.Stop();
            _wait.Start();
        };
        _all.IsCheckedChanged += (_, _) => Search();
        _case.IsCheckedChanged += (_, _) => Search();
        _regex.IsCheckedChanged += (_, _) => Search();
        _only.IsCheckedChanged += (_, _) => Render(false);
        _query.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                Next(e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
            }
        };
        _newer = Btn("New lines", () =>
        {
            _window = false;
            Load(true, update: true);
        }, true, "chevron_down")
            .With(b =>
            {
                b.IsVisible = false;
                b.HorizontalAlignment = HorizontalAlignment.Right;
                b.VerticalAlignment = VerticalAlignment.Bottom;
                b.Margin = new Thickness(0, 0, Space.XXL, Space.XL);
                ToolTip.SetTip(b, "The file grew: show the newest lines");
            });
    }

    private bool CanRead => _file != null && (LogParsers.Find(_file.Format) != null || LineLogs.Is(_file.Format));
    private bool IsLines => LineLogs.Is(_file?.Format);
    private bool ShowReadable => Readable && CanRead;

    public Control Build(LogFile? f, Action rebuild)
    {
        _rebuild = rebuild;
        if (f?.Path != _file?.Path)
        {
            _code?.Dispose();
            _code = null;
            _window = false;
            _hits.Clear();
        }
        _file = f;
        if (f == null)
        {
            return Card(Txt("Select a log file.", TextSize.Body, false, Muted));
        }

        var mode = CanRead ? ModeSwitch(rebuild) : null;
        // Search, the view switch, then the file buttons. Follow is a switch on the Files card. F5 reloads (no Reload button: the folder is watched).
        var tools = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var c in new Control?[] { _query.Free(), _case.Free(), _regex.Free(), Several ? _all.Free() : null, ShowReadable ? Btn("Next error", () => NextError(1), false, "warning").With(b => ToolTip.SetTip(b, "Go to the next error (F8; Shift+F8: the previous one)")) : null, ShowReadable ? null : _only.Free(), mode, ExportButton(), BookmarksButton(),
                     IconBtn("open", "Open file", () => PathUtil.OpenFile(f.Path)), IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(f.Path)) })
        {
            if (c != null)
            {
                tools.Children.Add(c.With(k =>
                {
                    k.Margin = new Thickness(0, 0, Space.S, Space.S);
                    k.Height = Theme.InputHeight;
                }));   // one height for every control in the row
            }
        }

        var head = new StackPanel
        {
            Spacing = Space.M,
            Margin = new Thickness(0, 0, 0, Space.S),
            Children = { Row(Title(Path.GetFileName(f.Path), TextSize.Subtitle), _status.Free().With(s => s.VerticalAlignment = VerticalAlignment.Center)), tools }
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = Space.L,
            Height = Heights.ViewerL
        };
        _newer.IsVisible = false;
        body.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(Radius.M),
            ClipToBounds = true,
            Child = new Panel { Children = { _view.Free(), _newer.Free() } }
        });
        body.Children.Add(_results.Free().With(r => Grid.SetColumn(r, 1)));
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(body);
        LoadAsync(true);
        return Card(dock);
    }

    private const int ReadableBytes = 8 << 20, RawBytes = 256 * 1024;

    /// <summary>Highlight time for new content, from Settings (0 = off).</summary>
    private double FadeSeconds => Math.Clamp(_x.S.Settings.FadeSeconds, 0, 60);
}
