// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// Splits a message into the text that was written and the blocks that were injected around it:
/// &lt;INSTRUCTIONS&gt;…&lt;/INSTRUCTIONS&gt;, &lt;environment_context&gt;, &lt;system-reminder&gt; and &lt;!-- name:begin --&gt;…&lt;!-- name:end --&gt; markers.
/// Tags count when they are ALL CAPS or have an underscore/hyphen in the name, so ordinary HTML in a message stays text.
/// </summary>
public static partial class ContextBlocks
{
    /// <param name="Label">null = normal text; otherwise the tag or marker name of an injected block.</param>
    public record Part(string? Label, string Text);

    [GeneratedRegex(@"<!--\s*(?<n>[\w:.\-]+?):begin\s*-->(?<b>.*?)<!--\s*\k<n>:end\s*-->|<(?<t>[A-Z][A-Z0-9_\-]{2,}|[a-z][a-z0-9]*(?:[_\-][a-z0-9]+)+)(?:\s[^>]*)?>(?<b2>.*?)</\k<t>>", RegexOptions.Singleline)]
    private static partial Regex Block();

    public static List<Part> Split(string text)
    {
        var parts = new List<Part>(); var at = 0;
        foreach (Match m in Block().Matches(text))
        {
            if (m.Index > at && text[at..m.Index].Trim() != "")
            {
                parts.Add(new(null, text[at..m.Index].Trim('\n')));
            }

            var label = m.Groups["n"].Success ? m.Groups["n"].Value : m.Groups["t"].Value;
            parts.Add(new(label, (m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["b2"].Value).Trim('\n')));
            at = m.Index + m.Length;
        }
        if (at < text.Length && text[at..].Trim() != "")
        {
            parts.Add(new(null, text[at..].Trim('\n')));
        }

        return parts;
    }
}
