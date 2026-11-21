// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Avalonia.Controls;

namespace AiStackprint.Ui;

internal partial class LogsView
{
    private void LoadFiles(AppDefinition def, ContentControl pane) => Fire.Forget(LoadFilesAsync(def, pane));

    private async Task LoadFilesAsync(AppDefinition def, ContentControl pane)
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
        _viewer.Release();
        _summary.Release();
        _viewer = new LogViewer(x)
        {
            Readable = _viewer.Readable,
            Compact = _viewer.Compact,
            Follow = _viewer.Follow
        };
        Wire(_viewer);
        _summary = new LogSummaryCard { Days = _summary.Days };
        _table.Content = null;
        _table = new ContentControl();
        _files = [];
        _filesApp = null;
        _checked.Clear();
        _file = null;
        _viewerHost.Content = null;
        _viewerHost = new ContentControl();
        _scroll = null;
        _follow = null;
    }
}
