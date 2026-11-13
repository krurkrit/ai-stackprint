using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using AiStackprint.Models;
using AiStackprint.Services;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Logs page: apps on the left; for the selected app a summary, its log files (select, delete to Trash) and the viewer.
/// The selected app's log folders are watched while the page is open: new, removed and renamed files update the list,
/// and a file that grows gets its size and time updated in place.
/// </summary>
class LogsView(Ctx x)
{
    string? _app;
    LogFile? _file;
    List<LogFile> _files = new();
    readonly HashSet<string> _checked = new();
    LogViewer _viewer = new(x);
    LogSummaryCard _summary = new();
    ContentControl _table = new();
    LogFolderWatcher? _watcher;
    string? _watching;   // app whose folders are watched

    IEnumerable<AppDefinition> Apps => x.S.Defs.Where(d => d.Logs.Count > 0 && x.ShownApps.Any(a => a.Id == d.Id));

    public Control Build(string? appId)
    {
        if (appId != null && appId != _app) { _app = appId; _file = null; _checked.Clear(); }
        _app ??= Apps.FirstOrDefault(d => x.Info(d.Id)?.Installed == true)?.Id ?? Apps.FirstOrDefault()?.Id;
        var def = _app == null ? null : x.S.Def(_app);
        Relist(def);

        var apps = Table.Build(new[] { new Col("App", "*") }, Apps.OrderByDescending(d => x.Info(d.Id)?.Installed == true).ThenBy(d => d.Name)
            .Select(d => new TableRow(new Control[] { AppName(d.Logo, d.Category, d.Name, 24) }, () => x.Go("Logs:" + d.Id), d.Id == _app)), "No app has log locations.");

        var head = new StackPanel { Spacing = 6, Children = { def == null ? H1("Logs") : H1("Logs", "Logs", def.Name) } };
        if (def != null) head.Children.Add(Txt("Looks in: " + string.Join("  ·  ", def.Logs.Where(l => PathUtil.OsMatches(l.Os)).Select(l => l.Path)) + "  ·  watching for changes · F5 reads again", 12, false, Muted));

        var right = new StackPanel { Spacing = 14, Margin = new Thickness(12, 28, 32, 28), Children = { head } };
        if (def != null)
        {
            right.Children.Add(_summary.Build(_files));
            // Follow first, then the destructive buttons together
            var del = new List<Control>();
            foreach (var days in new[] { 7, 30, 90 })
                del.Add(Btn($"Older than {days} d", () => Delete(LogTrash.OlderThan(_files, days))));
            del.Add(Btn("Delete selected", () => Delete(_files.Where(f => _checked.Contains(f.Path)).ToList()), false, "delete"));
            var follow = new ToggleSwitch { OnContent = "Follow newest", OffContent = "Follow newest", IsChecked = _viewer.Follow };
            ToolTip.SetTip(follow, "Open the newest log file, switch to a new file when one appears, and stay at the end. Clicking an older file turns it off.");
            follow.IsCheckedChanged += (_, _) =>
            {
                if ((follow.IsChecked == true) == _viewer.Follow) return;
                _viewer.Follow = follow.IsChecked == true;
                if (_viewer.Follow) _file = _files.FirstOrDefault();
                x.Go("Logs:" + _app);
            };
            right.Children.Add(Section("document_text", "Files", new ScrollViewer { MaxHeight = 260, Content = _table.Free() }, del.Prepend(follow).ToArray()));
            FillTable(def);
        }
        right.Children.Add(_viewer.Build(_file, () => x.Go("Logs:" + _app)));

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*") };
        var left = new ScrollViewer { Margin = new Thickness(32, 28, 12, 20), Content = Card(apps) };
        var scroll = new ScrollViewer { Content = right };
        Grid.SetColumn(scroll, 1);
        g.Children.Add(left); g.Children.Add(scroll);
        g.AttachedToVisualTree += (_, _) => Watch(def);
        g.DetachedFromVisualTree += (_, _) => Watch(null);
        return g;
    }

    void Relist(AppDefinition? def)
    {
        _files = def == null ? new() : LogReader.Find(def, x.S.Projects);
        if (_file != null) _file = _files.FirstOrDefault(f => f.Path == _file.Path);
        _file ??= _files.FirstOrDefault();
        _checked.IntersectWith(_files.Select(f => f.Path));
    }

    void FillTable(AppDefinition def)
    {
        var rows = _files.Select(f =>
        {
            var check = new CheckBox { IsChecked = _checked.Contains(f.Path) };
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) _checked.Add(f.Path); else _checked.Remove(f.Path); };
            return new TableRow(new Control[]
            {
                check, Table.NameDesc(Path.GetFileName(f.Path), Path.GetDirectoryName(f.Path) ?? ""), Txt(f.Label, 13),
                Txt(LogViewer.Size(f.Size), 13), Txt(f.Modified.ToString("yyyy-MM-dd HH:mm"), 13),
                IconBtn("delete", "Move to Recycle Bin / Trash", () => Delete(new() { f }))
            }, () => { _file = f; if (f.Path != _files.FirstOrDefault()?.Path) _viewer.Follow = false; x.Go("Logs:" + f.App); }, f.Path == _file?.Path);
        });
        _table.Content = Table.Build(new[] { new Col("", "32"), new Col("File", "2.4*"), new Col("Type", ".8*"), new Col("Size", ".7*", true), new Col("Modified", "1.1*"), new Col("", "40") }, rows,
            "No log files found." + (def.Logs.Any(l => l.Level == "Project") && x.S.Projects.Count == 0 ? " This app logs per project: add a project folder." : ""));
    }

    /// <summary>Watches the selected app's log folders. Stops when another app is chosen or the page closes.</summary>
    void Watch(AppDefinition? def)
    {
        if (def?.Id == _watching && _watcher != null) return;
        _watcher?.Dispose(); _watcher = null; _watching = def?.Id;
        if (def == null) return;
        _watcher = new LogFolderWatcher(LogReader.Roots(def, x.S.Projects), _files.FirstOrDefault()?.Path);   // _files is newest first
        _watcher.Changed += b => Dispatcher.UIThread.Post(() => OnChanged(def, b));
    }

    /// <summary>
    /// Files were added, removed or renamed: find the list again. Otherwise update the rows that changed in place
    /// (size and time), so the list does not jump. The open file reloads or offers "New lines".
    /// </summary>
    void OnChanged(AppDefinition def, LogFolderWatcher.Batch b)
    {
        if (_watching != def.Id) return;
        var open = _file is { } f ? (f.Path, f.Size, f.Modified) : default;
        var paths = b.Paths.Select(p => p.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p).ToHashSet(StringComparer.OrdinalIgnoreCase);   // SQLite writes land in the -wal file
        bool relist = b.Structure;
        if (!relist)
            foreach (var p in paths)
            {
                int i = _files.FindIndex(lf => string.Equals(lf.Path, p, StringComparison.OrdinalIgnoreCase));
                if (i < 0) continue;
                if (LogReader.Restat(_files[i]) is { } now) _files[i] = now; else relist = true;
            }
        if (relist) Relist(def);
        else
        {
            _files = _files.OrderByDescending(lf => lf.Modified).ToList();
            if (_file != null) _file = _files.FirstOrDefault(lf => lf.Path == _file.Path);
        }
        if (_viewer.Follow && _files.FirstOrDefault() is { } newest) _file = newest;   // a new file takes over while following
        FillTable(def);
        _summary.Update(_files);
        if (_file?.Path != open.Path) { x.Go("Logs:" + def.Id); return; }   // the open file is gone: show the newest
        if (_file != null && (_file.Path, _file.Size, _file.Modified) != open) _viewer.FileChanged();
    }

    /// <summary>F5 on the Logs page: find the files again and re-read the open one.</summary>
    public void Reload()
    {
        if (_app == null || x.S.Def(_app) is not { } def) return;
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
        _files = new(); _checked.Clear(); _file = null;
    }

    async void Delete(List<LogFile> files)
    {
        if (files.Count == 0) { await Dialogs.Message(x.Owner, "Delete logs", "No files selected."); return; }
        var active = files.Where(f => LogTrash.IsActive(f)).ToList();
        var msg = $"Move {ReadableLog.Plural(files.Count, "file")} ({LogViewer.Size(files.Sum(f => f.Size))}) to the {(OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash")}? You can restore them from there.";
        if (files.Any(f => f.Format != null)) msg += "\nSession logs also disappear from the app's resume/history list.";
        if (active.Count > 0) msg += $"\n{ReadableLog.Plural(active.Count, "file")} changed in the last minute and may still be in use by an open session.";
        msg += "\n\n" + string.Join("\n", files.Take(8).Select(f => Path.GetFileName(f.Path))) + (files.Count > 8 ? $"\n… and {files.Count - 8} more" : "");
        if (!await Dialogs.Confirm(x.Owner, "Delete logs", msg, "Move to " + (OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash"), true)) return;
        var (moved, failed) = LogTrash.Delete(files.Select(f => f.Path), _files);
        _checked.ExceptWith(moved);
        if (failed.Count > 0)
            await Dialogs.Message(x.Owner, "Some files were not deleted", string.Join("\n", failed.Select(f => $"{Path.GetFileName(f.Path)}: {f.Reason}")));
        x.Go("Logs:" + _app);
    }
}
