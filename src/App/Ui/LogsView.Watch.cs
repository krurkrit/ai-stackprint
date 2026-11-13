// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Avalonia.Threading;

namespace AiStackprint.Ui;

partial class LogsView
{
    /// <summary>Watches the selected app's log folders. Stops when another app is chosen or the page closes.</summary>
    void Watch(AppDefinition? def)
    {
        if (def?.Id == _watching && _watcher != null)
        {
            return;
        }

        _watcher?.Dispose(); _watcher = null; _watching = def?.Id;
        if (def == null)
        {
            return;
        }

        _watcher = new LogFolderWatcher(LogReader.Roots(def, x.S.Projects), _files.FirstOrDefault()?.Path);   // _files is newest first
        _watcher.Changed += b => Dispatcher.UIThread.Post(() => OnChanged(def, b));
    }

    /// <summary>
    /// Files were added, removed or renamed: find the list again. Otherwise update the rows that changed in place
    /// (size and time), so the list does not jump. The open file reloads or offers "New lines".
    /// </summary>
    void OnChanged(AppDefinition def, LogFolderWatcher.Batch b)
    {
        if (_watching != def.Id)
        {
            return;
        }

        var open = _file is { } f ? (f.Path, f.Size, f.Modified) : default;
        var paths = b.Paths.Select(p => p.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p).ToHashSet(StringComparer.OrdinalIgnoreCase);   // SQLite writes land in the -wal file
        bool relist = b.Structure;
        if (!relist)
        {
            foreach (var p in paths)
            {
                int i = _files.FindIndex(lf => string.Equals(lf.Path, p, StringComparison.OrdinalIgnoreCase));
                if (i < 0)
                {
                    continue;
                }

                if (LogReader.Restat(_files[i]) is { } now)
                {
                    _files[i] = now;
                }
                else
                {
                    relist = true;
                }
            }
        }

        if (relist)
        {
            Relist(def);
        }
        else
        {
            _files = _files.OrderByDescending(lf => lf.Modified).ToList();
            if (_file != null)
            {
                _file = _files.FirstOrDefault(lf => lf.Path == _file.Path);
            }
        }
        if (_viewer.Follow && _files.FirstOrDefault() is { } newest)
        {
            _file = newest;   // a new file takes over while following
        }

        FillTable(def);
        _summary.Update(_files);
        if (_file?.Path != open.Path) { x.Go("Logs:" + def.Id); return; }   // the open file is gone: show the newest
        if (_file != null && (_file.Path, _file.Size, _file.Modified) != open)
        {
            _viewer.FileChanged();
        }
    }
}
