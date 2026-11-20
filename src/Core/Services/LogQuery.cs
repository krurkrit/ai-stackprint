// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// What to find in a log. Text: words that must all be in the line or step, in any order; "quoted words" as one phrase.
/// Regular expression: the whole text is one pattern. In the conversation view role:user and tool:bash keep only those steps.
/// </summary>
public sealed partial class LogQuery
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(200);
    private readonly Regex[] _parts;

    private LogQuery(Regex[] parts, string? role, string? tool)
    {
        _parts = parts;
        Role = role;
        Tool = tool;
    }

    /// <summary>role: value (user, assistant, thinking, tool, result, system), or null.</summary>
    public string? Role { get; }

    /// <summary>tool: value (Bash, Read, apply_patch …), or null.</summary>
    public string? Tool { get; }

    /// <summary>The query, or null when it is empty. Throws ArgumentException for an invalid regular expression.</summary>
    /// <param name="steps">The conversation view: role: and tool: are filters. In the raw file they are searched as written.</param>
    public static LogQuery? Parse(string? text, bool matchCase, bool regex, bool steps)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var options = (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant;
        if (regex)
        {
            return new LogQuery([new Regex(text, options, _timeout)], null, null);
        }

        string? role = null, tool = null;
        var parts = new List<Regex>();
        foreach (Match m in Token().Matches(text))
        {
            var field = m.Groups["field"].Value.ToLowerInvariant();
            var value = m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["word"].Value;
            if (steps && field is "role" or "tool")
            {
                if (value.Length == 0)
                {
                    continue;   // still typing
                }

                if (field == "role")
                {
                    role = value;
                }
                else
                {
                    tool = value;
                }

                continue;
            }

            value = m.Groups["field"].Success ? m.Value.Trim('"') : value;   // not a field here: the text as written
            if (value.Length > 0)
            {
                parts.Add(new Regex(Regex.Escape(value), options, _timeout));
            }
        }

        return parts.Count == 0 && role == null && tool == null ? null : new LogQuery([.. parts], role, tool);
    }

    public static LogQuery? Parse(string? text, bool matchCase, bool regex) => Parse(text, matchCase, regex, false);

    /// <summary>Every word, phrase or the pattern is in <paramref name="text"/>.</summary>
    public bool IsMatch(string text) => IsMatch(text.AsSpan());

    /// <inheritdoc cref="IsMatch(string)"/>
    public bool IsMatch(ReadOnlySpan<char> text)
    {
        try
        {
            foreach (var p in _parts)
            {
                if (!p.IsMatch(text))
                {
                    return false;
                }
            }

            return true;
        }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>A conversation step matches: its role and tool (when asked for), and each word is in its tool, text or detail (no copy of them is made).</summary>
    public bool IsMatch(LogEntry e)
    {
        if ((Role != null && !e.Role.Contains(Role, StringComparison.OrdinalIgnoreCase)) || (Tool != null && !(e.Tool ?? "").Contains(Tool, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            foreach (var p in _parts)
            {
                if (!p.IsMatch(e.Text) && !(e.Detail is { } d && p.IsMatch(d)) && !(e.Tool is { } t && p.IsMatch(t)))
                {
                    return false;
                }
            }

            return true;
        }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>Where each word, phrase or pattern occurs in <paramref name="text"/>, overlaps joined, in order (to highlight; part of a line may hold only some words).</summary>
    public IReadOnlyList<(int Start, int Length)> Spans(string text)
    {
        var found = new List<(int Start, int End)>();
        try
        {
            foreach (var p in _parts)
            {
                found.AddRange(p.Matches(text).Where(m => m.Length > 0).Select(m => (m.Index, m.Index + m.Length)));
            }
        }
        catch (RegexMatchTimeoutException) { return []; }

        var spans = new List<(int Start, int Length)>();
        foreach (var (s, e) in found.OrderBy(f => f.Start))
        {
            if (spans.Count > 0 && s <= spans[^1].Start + spans[^1].Length)
            {
                spans[^1] = (spans[^1].Start, Math.Max(spans[^1].Start + spans[^1].Length, e) - spans[^1].Start);
            }
            else
            {
                spans.Add((s, e - s));
            }
        }

        return spans;
    }

    /// <summary>The first match in <paramref name="text"/> when all parts match (to scroll to and select it).</summary>
    public (int Start, int Length)? First(string text) => IsMatch(text) && Spans(text) is { Count: > 0 } s ? s[0] : null;

    [GeneratedRegex("""(?:(?<field>[A-Za-z]+):)?(?:"(?<quoted>[^"]*)"?|(?<word>[^\s"]*))""")]
    private static partial Regex Token();
}
