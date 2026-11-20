// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The Files card: the list (newest first, a page at a time), opening a file in the viewer, and deleting to the Recycle Bin / Trash.</summary>
internal partial class LogsView
{
    private void FillTable(AppDefinition def)
    {
        var q = SearchQuery.Parse(_fileQuery);
        var match = _files.Where(f => q.InText($"{Path.GetFileName(f.Path)} {f.Label} {Path.GetDirectoryName(f.Path)}")).ToList();
        var shown = match.Take(Math.Max(_rows, match.FindIndex(f => f.Path == _file?.Path) + 1)).ToList();   // newest first; the open file is always listed
        var rows = shown.Select(f =>
        {
            var check = new CheckBox { IsChecked = _checked.Contains(f.Path) };
            check.IsCheckedChanged += (_, _) =>
            {
                if (check.IsChecked == true)
                {
                    _checked.Add(f.Path);
                }
                else
                {
                    _checked.Remove(f.Path);
                }
            };
            return new TableRow(
            [
                check, Table.NameDesc(Path.GetFileName(f.Path), f.Label + "  ·  " + Path.GetDirectoryName(f.Path), marks: q),   // type on the second line: the name needs the width
                Txt(LogViewer.Size(f.Size), TextSize.Small), Txt(f.Modified.ToString("yyyy-MM-dd HH:mm"), TextSize.Small),
                IconBtn("delete", "Move to Recycle Bin / Trash", () => Delete([f]))
            ], () => Open(def, f), f.Path == _file?.Path);
        });
        var table = Table.Build([new Col("", "32"), new Col("File", "*"), new Col("Size", "76", true), new Col("Modified", "130"), new Col("", "40")], rows,
            q.IsEmpty || _files.Count == 0 ? "No log files found." + (def.Logs.Any(l => l.Level == "Project") && x.S.Projects.Count == 0 ? " This app logs per project: add a project folder." : "") : "No files match the filter.");
        var rest = match.Count - shown.Count;
        _table.Content = rest == 0 ? table : new StackPanel
        {
            Spacing = Space.M,
            Children = { table, Btn($"Show {Math.Min(rest, RowsStep)} more ({rest} older not shown)", () =>
            {
                _rows += RowsStep;
                FillTable(def);
            }) }
        };
    }

    /// <summary>Rows built at first: an app can have hundreds of log files, and every row is several controls rebuilt on each change the watcher sees.</summary>
    private const int RowsStep = 50;
    private int _rows = RowsStep;

    /// <summary>A click on a file: the selection moves and only the viewer changes, so the page stays where it is.</summary>
    private void Open(AppDefinition def, LogFile f)
    {
        if (f.Path == _file?.Path)
        {
            return;
        }

        _file = f;
        if (f.Path != _files.FirstOrDefault()?.Path)
        {
            _viewer.Follow = false;   // an older file: stop following (the switch's handler sees no change)
            _follow?.IsChecked = false;
        }

        FillTable(def);
        _viewerHost.Content = _viewer.Build(_file, () => x.Go("Logs:" + _app));
    }

    private void Delete(List<LogFile> files) => Fire.Forget(DeleteAsync(files));

    private async Task DeleteAsync(List<LogFile> files)
    {
        if (files.Count == 0)
        {
            await Dialogs.Message(x.Owner, "Delete logs", "No files selected.");
            return;
        }
        var active = files.Where(f => LogTrash.IsActive(f)).ToList();
        var msg = $"Move {ReadableLog.Plural(files.Count, "file")} ({LogViewer.Size(files.Sum(f => f.Size))}) to the {(OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash")}? You can restore them from there.";
        if (files.Any(f => f.Format != null))
        {
            msg += "\nSession logs also disappear from the app's resume/history list.";
        }

        if (active.Count > 0)
        {
            msg += $"\n{ReadableLog.Plural(active.Count, "file")} changed in the last minute and may still be in use by an open session.";
        }

        msg += "\n\n" + string.Join("\n", files.Take(8).Select(f => Path.GetFileName(f.Path))) + (files.Count > 8 ? $"\n… and {files.Count - 8} more" : "");
        if (!await Dialogs.Confirm(x.Owner, "Delete logs", msg, "Move to " + (OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash"), true))
        {
            return;
        }

        var (moved, failed) = LogTrash.Delete(files.Select(f => f.Path), _files);
        _checked.ExceptWith(moved);
        if (failed.Count > 0)
        {
            await Dialogs.Message(x.Owner, "Some files were not deleted", string.Join("\n", failed.Select(f => $"{Path.GetFileName(f.Path)}: {f.Reason}")));
        }

        x.Go("Logs:" + _app);
    }
}
