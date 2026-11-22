// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Services;

/// <summary>
/// Steps by their place: the file they come from (null = the session file) and the line they start on. A search result, a bookmark or an error is such a place; this finds
/// the step. <see cref="Find"/> takes the last step line at or before the line asked for (a reply that spans lines holds them all), so a line in the middle of a step finds
/// that step. Several steps can start on one line (a reply with text and a tool call): all of them are returned.
/// </summary>
public sealed class StepIndex
{
    private readonly ILookup<(string Source, long Line), LogEntry> _at;
    private readonly Dictionary<string, long[]> _lines;

    public StepIndex(IEnumerable<LogEntry> steps)
    {
        var placed = steps.Where(e => e.Line > 0).ToList();
        _at = placed.ToLookup(e => (e.Source ?? "", e.Line));
        _lines = placed.GroupBy(e => e.Source ?? "").ToDictionary(g => g.Key, g => g.Select(e => e.Line).Distinct().Order().ToArray());
    }

    /// <summary>True when some step of this file starts at or before the line (so the line is not older than everything kept).</summary>
    public bool Covers(string? source, long line) => _lines.TryGetValue(source ?? "", out var lines) && lines.Length > 0 && line >= lines[0];

    /// <summary>The steps that start on the last step line at or before <paramref name="line"/>; empty when there is none.</summary>
    public IReadOnlyList<LogEntry> Find(string? source, long line)
    {
        if (!_lines.TryGetValue(source ?? "", out var lines))
        {
            return [];
        }

        var i = Array.BinarySearch(lines, line);
        i = i >= 0 ? i : ~i - 1;
        return i < 0 ? [] : [.. _at[(source ?? "", lines[i])]];
    }
}
