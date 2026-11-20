// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// HTML inside Markdown (README badges, &lt;details&gt;, &lt;br&gt;, &lt;b&gt;, tables) turned into the Markdown <see cref="Blocks"/> reads,
/// and one-line plain text for descriptions. Code (fences, `spans`, &lt;pre&gt;, &lt;code&gt;) is kept as written. Only known HTML element
/// names are removed, so text such as "List&lt;string&gt;" stays.
/// </summary>
public static partial class MarkdownLite
{
    private const RegexOptions HtmlOptions = RegexOptions.IgnoreCase | RegexOptions.Singleline;
    private const string Known = "a|abbr|address|article|aside|b|bdi|bdo|big|blockquote|body|br|caption|center|cite|code|col|colgroup|dd|del|details|dfn|div|dl|dt|em|"
        + "figcaption|figure|font|footer|h[1-6]|head|header|hr|html|i|img|ins|kbd|li|main|mark|nav|ol|p|picture|pre|q|s|samp|section|small|source|span|strike|"
        + "strong|sub|summary|sup|table|tbody|td|tfoot|th|thead|tr|tt|u|ul|wbr";

    [GeneratedRegex(@"<!--.*?-->|<(script|style|svg|head)\b[^>]*>.*?</\1\s*>", HtmlOptions)] private static partial Regex Dropped();
    [GeneratedRegex(@"<pre\b[^>]*>\s*(?:<code\b(?:[^>]*?\bclass\s*=\s*[""'](?:language-|lang-)?(?<lang>[\w+#.-]+)[^""']*[""'])?[^>]*>)?(?<code>.*?)(?:</code\s*>)?\s*</pre\s*>", HtmlOptions)]
    private static partial Regex Pre();
    [GeneratedRegex(@"<(code|kbd|tt|samp)\b[^>]*>(.*?)</\1\s*>", HtmlOptions)] private static partial Regex InlineCode();
    [GeneratedRegex(@"<img\b[^>]*?>", HtmlOptions)] private static partial Regex Img();
    [GeneratedRegex(@"\balt\s*=\s*(?:""([^""]*)""|'([^']*)')", HtmlOptions)] private static partial Regex Alt();
    [GeneratedRegex(@"<a\b[^>]*?\bhref\s*=\s*(?:""([^""]*)""|'([^']*)')[^>]*>(.*?)</a\s*>", HtmlOptions)] private static partial Regex Anchor();
    [GeneratedRegex(@"<h([1-6])\b[^>]*>(.*?)</h\1\s*>", HtmlOptions)] private static partial Regex HtmlHeading();
    [GeneratedRegex(@"<(b|strong|summary|dt)\b[^>]*>(.*?)</\1\s*>", HtmlOptions)] private static partial Regex Strong();
    [GeneratedRegex(@"<(i|em|cite|dfn)\b[^>]*>(.*?)</\1\s*>", HtmlOptions)] private static partial Regex Emphasis();
    [GeneratedRegex(@"<table\b[^>]*>(.*?)</table\s*>", HtmlOptions)] private static partial Regex HtmlTable();
    [GeneratedRegex(@"<tr\b[^>]*>(.*?)</tr\s*>", HtmlOptions)] private static partial Regex HtmlRow();
    [GeneratedRegex(@"<t[hd]\b[^>]*>(.*?)</t[hd]\s*>", HtmlOptions)] private static partial Regex HtmlCell();
    [GeneratedRegex(@"<li\b[^>]*>", HtmlOptions)] private static partial Regex ListItem();
    [GeneratedRegex(@"<br\s*/?>", HtmlOptions)] private static partial Regex LineBreak();
    [GeneratedRegex(@"<hr\b[^>]*>", HtmlOptions)] private static partial Regex HtmlRule();
    [GeneratedRegex(@"</?(?:p|div|section|article|aside|header|footer|main|nav|center|details|blockquote|figure|figcaption|ul|ol|dl|dd|address|picture)\b[^>]*>", HtmlOptions)]
    private static partial Regex BlockTag();
    [GeneratedRegex(@"</?(?:" + Known + @")\b[^<>]*>", HtmlOptions)] private static partial Regex AnyKnown();
    [GeneratedRegex(@"\n[ \t]*\n(?:[ \t]*\n)+")] private static partial Regex BlankLines();
    [GeneratedRegex(@"\x01(\d+)\x01")] private static partial Regex Kept();
    [GeneratedRegex(@"(`+)[^`\n]*?\1")] private static partial Regex CodeSpan();
    [GeneratedRegex(@"`([^`\n]+)`|!?\[([^\]\n]*)\]\(([^)\s]+)\)|\*\*([^*\n]+?)\*\*|__([^_\n]+?)__")] private static partial Regex PlainToken();
    [GeneratedRegex(@"^\s{0,3}(?:#{1,6}\s+|>\s?|[-*+]\s+|\d+[.)]\s+)")] private static partial Regex LineMark();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    /// <summary>The Markdown with its HTML turned into Markdown; fenced code is left as it is.</summary>
    public static string FromHtml(string md) => FromHtml(md, false);

    /// <param name="plain">Bold and italic elements become their text, without Markdown marks.</param>
    private static string FromHtml(string md, bool plain)
    {
        if (!md.Contains('<') && !md.Contains('&'))
        {
            return md;
        }

        var sb = new StringBuilder(md.Length);
        var prose = new StringBuilder();
        string? fence = null;
        foreach (var line in md.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.TrimStart();
            if (fence == null && (t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith("~~~", StringComparison.Ordinal)))
            {
                sb.Append(Convert(prose.ToString(), plain));
                prose.Clear();
                fence = t[..3];
                sb.Append(line).Append('\n');
            }
            else if (fence != null)
            {
                sb.Append(line).Append('\n');
                if (t.StartsWith(fence, StringComparison.Ordinal))
                {
                    fence = null;
                }
            }
            else
            {
                prose.Append(line).Append('\n');
            }
        }

        sb.Append(Convert(prose.ToString(), plain));
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>One line of plain text for a description: HTML and Markdown marks removed, entities decoded, white space collapsed.</summary>
    public static string Plain(string text)
    {
        if (text.AsSpan().IndexOfAny("<&*_`[#>|\n\r") < 0)
        {
            return text.Trim();
        }

        var lines = FromHtml(text, true).Split('\n')
            .Where(l => !TableSep().IsMatch(l.Trim()))
            .Select(l => LineMark().Replace(l, "").Replace('|', ' '));
        var one = PlainToken().Replace(string.Join(" ", lines), m =>
            m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value);
        return Spaces().Replace(one, " ").Trim();
    }

    /// <summary>Prose without fences: code is set aside first (kept[i] behind \x01i\x01), so nothing inside it changes.</summary>
    private static string Convert(string s, bool plain)
    {
        if (s.Length == 0 || (!s.Contains('<') && !s.Contains('&')))
        {
            return s;
        }

        var kept = new List<string>();
        string Keep(string code)
        {
            kept.Add(code);
            return $"\x01{kept.Count - 1}\x01";
        }

        s = CodeSpan().Replace(s, m => Keep(m.Value));
        s = Dropped().Replace(s, "");
        s = Pre().Replace(s, m => "\n\n" + Keep($"```{m.Groups["lang"].Value}\n{Text(m.Groups["code"].Value, false).Trim('\n')}\n```") + "\n\n");
        s = InlineCode().Replace(s, m => Keep($"`{Text(m.Groups[2].Value, true)}`"));
        s = Img().Replace(s, m => Alt().Match(m.Value) is { Success: true } a ? (a.Groups[1].Success ? a.Groups[1].Value : a.Groups[2].Value) : "");
        s = Anchor().Replace(s, m =>
        {
            var url = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var label = OneLine(AnyKnown().Replace(m.Groups[3].Value, ""));
            return label == "" ? "" : url.StartsWith('#') || url == "" ? label : $"[{label}]({WebUtility.HtmlDecode(url)})";
        });
        s = HtmlTable().Replace(s, m => "\n\n" + Table(m.Groups[1].Value) + "\n\n");
        s = HtmlHeading().Replace(s, m => $"\n\n{new string('#', m.Groups[1].Value[0] - '0')} {OneLine(m.Groups[2].Value)}\n\n");
        s = Strong().Replace(s, m => Wrap(m.Groups[2].Value, plain ? "" : "**", m.Groups[1].Value.ToLowerInvariant() is "summary" or "dt"));
        s = Emphasis().Replace(s, m => Wrap(m.Groups[2].Value, plain ? "" : "*", false));
        s = ListItem().Replace(s, "\n- ");
        s = LineBreak().Replace(s, "\n");
        s = HtmlRule().Replace(s, "\n\n---\n\n");
        s = BlockTag().Replace(s, "\n\n");
        s = AnyKnown().Replace(s, "");
        s = WebUtility.HtmlDecode(s).Replace(' ', ' ');
        s = BlankLines().Replace(s, "\n\n");
        // A kept fence can hold kept spans (backticks inside <pre>): put them back too
        string Restore(string x) => Kept().Replace(x, m => Restore(kept[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]));
        return kept.Count == 0 ? s : Restore(s);
    }

    /// <summary>Bold or italic on one line, as Markdown marks need; a block (summary) also ends its paragraph.</summary>
    private static string Wrap(string inner, string mark, bool block)
    {
        var t = OneLine(inner);
        return t == "" ? "" : block ? $"\n\n{mark}{t}{mark}\n\n" : $"{mark}{t}{mark}";
    }

    /// <summary>A Markdown table from HTML rows: the first row is the header.</summary>
    private static string Table(string html)
    {
        var rows = HtmlRow().Matches(html).Select(r => HtmlCell().Matches(r.Groups[1].Value).Select(c => OneLine(AnyKnown().Replace(c.Groups[1].Value, "")).Replace('|', '/')).ToList())
            .Where(r => r.Count > 0).ToList();
        if (rows.Count == 0)
        {
            return OneLine(AnyKnown().Replace(html, " "));
        }

        var n = rows.Max(r => r.Count);
        string Line(List<string> r) => "| " + string.Join(" | ", Enumerable.Range(0, n).Select(i => i < r.Count ? r[i] : "")) + " |";
        return string.Join("\n", new[] { Line(rows[0]), "|" + string.Concat(Enumerable.Repeat(" --- |", n)) }.Concat(rows.Skip(1).Select(Line)));
    }

    /// <summary>The text of an element: tags removed and entities decoded (for code), optionally on one line.</summary>
    private static string Text(string html, bool oneLine)
    {
        var t = WebUtility.HtmlDecode(AnyKnown().Replace(html, ""));
        return oneLine ? OneLine(t) : t;
    }

    private static string OneLine(string s) => Spaces().Replace(s, " ").Trim();
}
