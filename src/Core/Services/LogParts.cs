// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// A text log that its app splits into numbered or dated files (<c>main.log</c>, <c>main1.log</c>, <c>main.old.log</c>, <c>app-2026-08-07.log</c>): files in the same
/// folder, of the same source and format, whose names are the same without their digits. The viewer reads them as one log, oldest first.
/// Sessions are never parts: each file is a conversation of its own.
/// </summary>
public static partial class LogParts
{
    /// <summary>The parts of the log <paramref name="f"/> belongs to, oldest first (by time written); just <paramref name="f"/> when it has none or is not a text log.</summary>
    public static List<LogFile> Of(LogFile f, IEnumerable<LogFile> files)
    {
        if (!LineLogs.Is(f.Format))
        {
            return [f];
        }

        var dir = Path.GetDirectoryName(f.Path);
        var key = Key(f.Path);
        var parts = files.Where(o => o.Format == f.Format && o.Label == f.Label && string.Equals(Path.GetDirectoryName(o.Path), dir, StringComparison.OrdinalIgnoreCase) && Key(o.Path) == key)
            .DistinctBy(o => o.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o.Modified).ThenBy(o => o.Path, StringComparer.Ordinal)
            .ToList();
        return parts.Any(p => string.Equals(p.Path, f.Path, StringComparison.OrdinalIgnoreCase)) ? parts : [f];
    }

    /// <summary>The name without digits and an <c>.old</c>: what the parts of one log share.</summary>
    internal static string Key(string path) => Digits().Replace(Path.GetFileName(path), "").Replace(".old", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

    [GeneratedRegex(@"[._-]?\d+")]
    private static partial Regex Digits();
}
