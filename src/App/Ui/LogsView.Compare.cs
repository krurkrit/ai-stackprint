// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>Compare: the two session files ticked in the Files list, read and shown side by side.</summary>
internal partial class LogsView
{
    private const int CompareBytes = 8 << 20;

    private void Compare() => Fire.Forget(CompareAsync());

    private async Task CompareAsync()
    {
        var picked = _files.Where(f => _checked.Contains(f.Path) && LogParsers.Find(f.Format) != null).ToList();
        if (picked.Count != 2 || _checked.Count != 2)
        {
            await Dialogs.Message(x.Owner, "Compare sessions", "Tick two session files in the list (the boxes on the left), then press Compare.");
            return;
        }

        var (first, second) = picked[0].Modified >= picked[1].Modified ? (picked[1], picked[0]) : (picked[0], picked[1]);   // the older on the left
        Toast.Show("Reading the two sessions…", "info");
        var (a, b) = await Task.Run(() => (Read(first), Read(second)));
        await Dialogs.Compare(x.Owner, Title(first), a, Title(second), b);
    }

    private static Session Read(LogFile f) => LogParsers.Find(f.Format)!.Parse(LogReader.Utf8Lines(f.Path, CompareBytes));

    private static string Title(LogFile f) => $"{f.Modified:yyyy-MM-dd HH:mm} · {Path.GetFileNameWithoutExtension(f.Path)}";
}
