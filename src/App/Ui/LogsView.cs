// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Logs page: apps on the left; for the selected app a summary, its log files (select, delete to Trash) and the viewer.
/// The selected app's log folders are watched while the page is open: new, removed and renamed files update the list,
/// and a file that grows gets its size and time updated in place.
/// </summary>
internal partial class LogsView(Ctx x)
{
    private string? _app;
    private LogFile? _file;
    private List<LogFile> _files = [];
    private readonly HashSet<string> _checked = [];
    private LogViewer _viewer = new(x);
    private LogSummaryCard _summary = new();
    private ContentControl _table = new();
    private LogFolderWatcher? _watcher;
    private string? _watching;   // app whose folders are watched
    private string _fileQuery = "";   // filters the Files list: words in the file name, type or folder

    private IEnumerable<AppDefinition> Apps => x.S.Defs.Where(d => d.Logs.Count > 0 && x.ShownApps.Any(a => a.Id == d.Id));

    public Control Build(string? appId)
    {
        var keep = appId == null || appId == _app ? _scroll?.Offset : null;   // the same app built again (view switch, follow, delete): stay where the user was
        if (appId != null && appId != _app) { _app = appId; _file = null; _checked.Clear(); _fileQuery = ""; _rows = RowsStep; }
        _app ??= Apps.FirstOrDefault(d => x.Info(d.Id)?.Installed == true)?.Id ?? Apps.FirstOrDefault()?.Id;
        var def = _app == null ? null : x.S.Def(_app);
        var loading = def != null && _filesApp != def.Id;   // when the app changes (or the page is opened) its folders are read in the background
        if (!loading)
        {
            _file ??= _files.FirstOrDefault();   // the files are already read (and kept current by the watcher): a click on a file does not read the folders again
        }

        var apps = Table.Build([new Col("App", "*")], Apps.OrderByDescending(d => x.Info(d.Id)?.Installed == true).ThenBy(d => d.Name)
            .Select(d => new TableRow([AppName(d.Logo, d.Category, d.Name, Sizes.LogoS)], () => x.Go("Logs:" + d.Id), d.Id == _app)), "No app has log locations.");

        var pane = new ContentControl { Content = BuildRight(def, loading) };

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Widths.SideList},*") };
        var left = new ScrollViewer { Margin = new Thickness(Space.Page, Space.XXXL, Space.L, Space.XXL), Content = Card(apps) };
        var scroll = _scroll = new ScrollViewer { Content = pane };
        if (keep is { } at && !loading)
        {
            Dispatcher.UIThread.Post(() => scroll.Offset = at, DispatcherPriority.Loaded);   // after the first layout, when the height is known
        }

        Grid.SetColumn(scroll, 1);
        g.Children.Add(left); g.Children.Add(scroll);
        if (loading)
        {
            LoadFiles(def!, pane);
        }

        g.AttachedToVisualTree += (_, _) => Watch(def);
        g.DetachedFromVisualTree += (_, _) => Watch(null);
        return g;
    }

    /// <summary>The right side: title, summary, files and viewer. While the files are still being read the cards are there and show the loading icon.</summary>
    private StackPanel BuildRight(AppDefinition? def, bool loading)
    {
        var head = new StackPanel { Spacing = Space.S, Children = { def == null ? H1("Logs") : H1("Logs", "Logs", def.Name) } };
        if (def != null)
        {
            head.Children.Add(Txt("Looks in: " + string.Join("  ·  ", def.Logs.Where(l => PathUtil.OsMatches(l.Os)).Select(l => l.Path)) + "  ·  watching for changes · F5 reads again", TextSize.Caption, false, Muted));
        }

        var right = new StackPanel { Spacing = Space.XL, Margin = new Thickness(Space.L, Space.XXXL, Space.Page, Space.XXXL), Children = { head } };
        if (def != null)
        {
            right.Children.Add(loading ? Section("data_pie", "Summary", Busy.Loading("Reading log folders…")) : _summary.Build(_files));
            // Follow first, then one Delete menu (four buttons did not fit on a line); each choice still asks before anything moves to the Recycle Bin
            var del = Btn("Delete…", () => { }, false, "delete");
            var menu = new MenuFlyout();
            void Choice(string text, Func<List<LogFile>> files)
            {
                var item = new MenuItem { Header = text };
                item.Click += (_, _) => Delete(files());
                menu.Items.Add(item);
            }

            Choice("Selected files", () => [.. _files.Where(f => _checked.Contains(f.Path))]);
            foreach (var days in new[] { 7, 30, 90 })
            {
                Choice($"Older than {days} days", () => LogTrash.OlderThan(_files, days));
            }

            del.Flyout = menu;
            var follow = _follow = new ToggleSwitch { OnContent = "Follow newest", OffContent = "Follow newest", IsChecked = _viewer.Follow };
            ToolTip.SetTip(follow, "Open the newest log file, switch to a new file when one appears, and stay at the end. Clicking an older file turns it off.");
            follow.IsCheckedChanged += (_, _) =>
            {
                if ((follow.IsChecked == true) == _viewer.Follow)
                {
                    return;
                }

                _viewer.Follow = follow.IsChecked == true;
                if (_viewer.Follow)
                {
                    _file = _files.FirstOrDefault();
                }

                x.Go("Logs:" + _app);
            };
            var search = SearchBox("Filter files by name, type or folder").With(b => { b.Text = _fileQuery; ToolTip.SetTip(b, "Words match separately, in any order; \"quotes\" match a phrase."); })
                .OnPause(q => { _fileQuery = q; _rows = RowsStep; FillTable(def); });
            right.Children.Add(Section("document_text", "Files", new StackPanel { Spacing = Space.L, Children = { search, new ScrollViewer { MaxHeight = Heights.List, Content = _table.Free() } } }, follow, del));
            if (loading)
            {
                _table.Content = Busy.Loading("Reading log folders…");
            }
            else
            {
                FillTable(def);
            }
        }
        _viewerHost = new ContentControl
        {
            Content = loading ? Card(Busy.Loading("Reading log folders…")).With(c => c.MinHeight = Heights.ViewerL) : _viewer.Build(_file, () => x.Go("Logs:" + _app))   // as tall as the viewer: nothing moves when it arrives
        };
        right.Children.Add(_viewerHost);
        return right;
    }

    private ScrollViewer? _scroll;
    private ToggleSwitch? _follow;
    private ContentControl _viewerHost = new();

    private string? _filesApp;   // the app whose files _files holds; the watcher keeps them current while the page is open

    private async void LoadFiles(AppDefinition def, ContentControl pane)
    {
        var projects = x.S.Projects.ToList();
        List<LogFile> found = [];
        try
        {
            found = await Task.Run(() => LogReader.Find(def, projects));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // unreadable folders: shown as no files

        if (_app != def.Id)
        {
            return;   // the user moved to another app meanwhile
        }

        _files = found;
        _filesApp = def.Id;
        _file = _files.FirstOrDefault(f => f.Path == _file?.Path) ?? _files.FirstOrDefault();
        _checked.IntersectWith(_files.Select(f => f.Path));
        pane.Content = BuildRight(def, false);   // the title and cards stay as they are; only what was loading fades in (the viewer and summary fade their own data)
        if (_table.Content is Control rows)
        {
            Reveal.Fade(rows);
        }
    }

    private void Relist(AppDefinition? def)
    {
        _files = def == null ? [] : LogReader.Find(def, x.S.Projects);
        if (_file != null)
        {
            _file = _files.FirstOrDefault(f => f.Path == _file.Path);
        }

        _file ??= _files.FirstOrDefault();
        _checked.IntersectWith(_files.Select(f => f.Path));
    }

    /// <summary>F5 on the Logs page: find the files again and re-read the open one.</summary>
    public void Reload()
    {
        if (_app == null || x.S.Def(_app) is not { } def)
        {
            return;
        }

        Relist(def);
        FillTable(def);
        _summary.Update(_files);
        _viewer.Reload();
    }

    /// <summary>
    /// Leaving the Logs page: stop watching and drop everything held for it (file list, viewer, summary).
    /// The viewer, summary and table are replaced, not just emptied: Avalonia's render tree of a control that is cleared
    /// while the page detaches keeps its old children, so a reused container kept the last log (and every code block's grammar) alive.
    /// Only the choices carry over: Conversation/Raw, Follow newest and the summary range.
    /// </summary>
    public void Release()
    {
        Watch(null);
        _viewer.Release(); _summary.Release();
        _viewer = new LogViewer(x) { Readable = _viewer.Readable, Follow = _viewer.Follow };
        _summary = new LogSummaryCard { Days = _summary.Days };
        _table.Content = null; _table = new ContentControl();
        _files = []; _filesApp = null; _checked.Clear(); _file = null;
        _viewerHost.Content = null; _viewerHost = new ContentControl(); _scroll = null; _follow = null;
    }
}
