// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>What the steps of one open log file can do: keep bookmarks (state.json; the rows that show a mark are told when one changes) and link to what a step points at.</summary>
/// <param name="canMark">Bookmarks need the steps' lines to be the file's lines (a file shown cut to its end has no bookmarks).</param>
/// <param name="link">The link for something a step points at (a model, a skill, a server, a file); null when there is nowhere to go.</param>
internal sealed class StepContext(UiState state, string file, bool canMark, Func<StepRef, (string Label, Action Go)?> link)
{
    private readonly Dictionary<LogEntry, Action> _watch = [];

    public string File => file;

    /// <summary>A step has a place in the file (its line) only when the file was read whole.</summary>
    public bool CanMark => canMark;

    /// <summary>A step can be bookmarked when it has a line in its file: a sub-agent's step always has (its own file was read whole), a step of the session file when the file was read whole.</summary>
    public bool CanMarkStep(LogEntry e) => e.Line > 0 && (e.Source != null || canMark);

    public Bookmark? Get(LogEntry e) => CanMarkStep(e) ? Bookmarks.Find(state, e.Source ?? file, e.Line) : null;

    /// <summary>The links of a step: one for each thing it points at that the app can show.</summary>
    public List<(string Label, Action Go)> Links(LogEntry e) => [.. StepReferences.Find(e).Select(r => link(r)).OfType<(string Label, Action Go)>()];

    public List<Bookmark> All() => Bookmarks.ForSession(state, file);

    /// <summary>Calls <paramref name="changed"/> when the step's bookmark changes (so its row and its dot in the graph can show or hide their mark; several can watch).</summary>
    public void Watch(LogEntry e, Action changed) => _watch[e] = _watch.TryGetValue(e, out var earlier) ? earlier + changed : changed;

    public void Set(LogEntry e, bool starred, string? note)
    {
        if (!CanMarkStep(e))
        {
            return;
        }

        Bookmarks.Set(state, e.Source ?? file, e.Line, ReadableLog.OneLine(e), starred, note);
        StateStore.Save(state);
        if (_watch.TryGetValue(e, out var changed))
        {
            changed();
        }
    }
}
