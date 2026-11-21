// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>The bookmarks of one open log file: reads and writes them (state.json) and tells the rows that show a mark when one changes.</summary>
internal sealed class BookmarkSource(UiState state, string file)
{
    private readonly Dictionary<LogEntry, Action> _watch = [];

    public string File => file;

    /// <summary>A step has a place in the file (its line) only when the file was read whole.</summary>
    public Bookmark? Get(LogEntry e) => e.Line > 0 ? Bookmarks.Find(state, file, e.Line) : null;

    public List<Bookmark> All() => Bookmarks.For(state, file);

    /// <summary>Calls <paramref name="changed"/> when the step's bookmark changes (so its row can show or hide its mark).</summary>
    public void Watch(LogEntry e, Action changed) => _watch[e] = changed;

    public void Set(LogEntry e, bool starred, string? note)
    {
        if (e.Line <= 0)
        {
            return;
        }

        Bookmarks.Set(state, file, e.Line, ReadableLog.OneLine(e), starred, note);
        StateStore.Save(state);
        if (_watch.TryGetValue(e, out var changed))
        {
            changed();
        }
    }
}
