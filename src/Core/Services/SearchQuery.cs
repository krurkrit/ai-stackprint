// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>One part of a search: a word or quoted phrase, for every field (<see cref="Field"/> null) or for one field.</summary>
/// <param name="Text">Lower case.</param>
/// <param name="Exact">Written in quotes after a field (author:"Jane Doe"): the whole value must match.</param>
public sealed record SearchTerm(string? Field, string Text, bool Exact);

/// <summary>
/// A search for skills and plugins as typed: words match separately and all must match; "quoted words" match as one phrase;
/// field:value matches one field only (name, desc, author, app, level, keyword or tag, path; has: what a plugin adds, e.g. has:mcp). See <see cref="ItemSearch"/>.
/// </summary>
public sealed partial class SearchQuery
{
    /// <summary>Field names the user can type, and the field each one means.</summary>
    public static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "name",
        ["desc"] = "desc",
        ["description"] = "desc",
        ["author"] = "author",
        ["by"] = "author",
        ["app"] = "app",
        ["level"] = "level",
        ["keyword"] = "keyword",
        ["keywords"] = "keyword",
        ["tag"] = "keyword",
        ["path"] = "path",
        ["has"] = "has",
    };

    public static readonly SearchQuery Empty = new([]);

    private SearchQuery(IReadOnlyList<SearchTerm> terms) { Terms = terms; }

    public IReadOnlyList<SearchTerm> Terms { get; }

    public bool IsEmpty => Terms.Count == 0;

    /// <summary>The words to mark in a name or description: free words and phrases, and name: or desc: values.</summary>
    public IReadOnlyList<string> Marks(string field) =>
        Terms.Where(t => t.Field == null || t.Field == field).Select(t => t.Text).Distinct().ToList();

    /// <summary>Every word or phrase occurs in <paramref name="text"/>, ignoring fields: for lists that have one text per row.</summary>
    public bool InText(string text) => Terms.All(t => text.Contains(t.Text, StringComparison.OrdinalIgnoreCase));

    public static SearchQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var terms = new List<SearchTerm>();
        foreach (Match m in Token().Matches(text))
        {
            var field = m.Groups["field"].Success && Fields.TryGetValue(m.Groups["field"].Value, out var f) ? f : null;
            var quoted = m.Groups["quoted"].Success;
            var value = (quoted ? m.Groups["quoted"].Value : m.Groups["word"].Value).Trim().ToLowerInvariant();
            if (m.Groups["field"].Success && field == null)
            {
                value = m.Value.Trim('"').ToLowerInvariant();   // not a field we know (a URL, a time): search the text as written
            }

            if (value.Length > 0)
            {
                terms.Add(new SearchTerm(field, value, field != null && quoted));
            }
        }

        return terms.Count == 0 ? Empty : new SearchQuery(terms);
    }

    /// <summary>Where <paramref name="words"/> occur in <paramref name="text"/>, ignoring case; overlapping matches joined, in order.</summary>
    public static IReadOnlyList<(int Start, int Length)> Spans(string text, IEnumerable<string> words)
    {
        var found = new List<(int Start, int End)>();
        foreach (var w in words.Where(w => w.Length > 0))
        {
            for (var i = text.IndexOf(w, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(w, i + w.Length, StringComparison.OrdinalIgnoreCase))
            {
                found.Add((i, i + w.Length));
            }
        }

        var spans = new List<(int Start, int Length)>();
        foreach (var (s, e) in found.OrderBy(f => f.Start))
        {
            if (spans.Count > 0 && s <= spans[^1].Start + spans[^1].Length)
            {
                var last = spans[^1];
                spans[^1] = (last.Start, Math.Max(last.Start + last.Length, e) - last.Start);
            }
            else
            {
                spans.Add((s, e - s));
            }
        }

        return spans;
    }

    [GeneratedRegex("""(?:(?<field>[A-Za-z]+):)?(?:"(?<quoted>[^"]*)"?|(?<word>[^\s"]*))""")]   // a bare "author:" (still typing) has no value and is skipped
    private static partial Regex Token();
}
