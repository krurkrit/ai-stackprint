// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Steps you starred or wrote a note on in the log viewer, kept in state.json (this machine only; never exported). A step is its file and its line in the file.</summary>
public static class Bookmarks
{
    /// <summary>The most kept; the oldest go first.</summary>
    public const int Max = 500;

    private static bool Same(Bookmark b, string file, long line) => b.Line == line && string.Equals(b.File, file, StringComparison.OrdinalIgnoreCase);

    public static Bookmark? Find(UiState s, string file, long line) => s.Bookmarks.FirstOrDefault(b => Same(b, file, line));

    /// <summary>The bookmarks of one file, in file order.</summary>
    public static List<Bookmark> For(UiState s, string file) =>
        [.. s.Bookmarks.Where(b => string.Equals(b.File, file, StringComparison.OrdinalIgnoreCase)).OrderBy(b => b.Line)];

    /// <summary>The bookmarks of a session file and of its sub-agents' files, in file order (the main file first).</summary>
    public static List<Bookmark> ForSession(UiState s, string file) =>
        [.. s.Bookmarks.Where(b => string.Equals(b.File, file, StringComparison.OrdinalIgnoreCase) || SessionAgents.AgentFiles(file).Contains(b.File, StringComparer.OrdinalIgnoreCase))
            .OrderBy(b => string.Equals(b.File, file, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(b => b.File, StringComparer.Ordinal).ThenBy(b => b.Line)];

    /// <summary>Stars a step and/or sets its note. A step that is neither starred nor noted is not kept.</summary>
    public static void Set(UiState s, string file, long line, string preview, bool starred, string? note)
    {
        s.Bookmarks.RemoveAll(b => Same(b, file, line));
        var text = note?.Trim();
        if (!starred && string.IsNullOrEmpty(text))
        {
            return;
        }

        s.Bookmarks.Add(new Bookmark
        {
            File = file,
            Line = line,
            Starred = starred,
            Note = string.IsNullOrEmpty(text) ? null : text,
            Preview = preview.Length > 160 ? preview[..160] : preview,
            Saved = DateTime.UtcNow
        });
        if (s.Bookmarks.Count > Max)
        {
            s.Bookmarks.RemoveRange(0, s.Bookmarks.Count - Max);
        }
    }
}
