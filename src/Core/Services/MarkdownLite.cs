using System.Text.RegularExpressions;

namespace AiStackprint.Services;

public enum MdKind { Paragraph, Heading, Bullet, Number, Quote, Rule, Table }

/// <param name="Level">Heading level, or list nesting depth.</param>
/// <param name="Rows">Table cells, header row first.</param>
/// <param name="Num">The number of a numbered item.</param>
public record MdBlock(MdKind Kind, string Text, int Level = 0, List<List<string>>? Rows = null, string? Num = null);

/// <param name="Url">Set for links.</param>
public record MdSpan(string Text, bool Bold, bool Italic, bool Code, string? Url);

/// <summary>Small Markdown reader for chat text: headings, lists, quotes, rules, tables and bold / italic / `code` / [links](url). Code fences are cut out by the caller.</summary>
public static partial class MarkdownLite
{
    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$")] private static partial Regex HeadingLine();
    [GeneratedRegex(@"^(?:-{3,}|\*{3,}|_{3,})$")] private static partial Regex RuleLine();
    [GeneratedRegex(@"^(\s*)([-*+])\s+(.*)$")] private static partial Regex BulletLine();
    [GeneratedRegex(@"^(\s*)(\d+)[.)]\s+(.*)$")] private static partial Regex NumberLine();
    [GeneratedRegex(@"^\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?$")] private static partial Regex TableSep();
    [GeneratedRegex(@"`(?<code>[^`\n]+)`|\[(?<lt>[^\]\n]+)\]\((?<url>[^)\s]+)\)|\*\*(?<b1>[^*\n]+?)\*\*|__(?<b2>[^_\n]+?)__|\*(?<i1>[^*\s][^*\n]*?)\*|(?<!\w)_(?<i2>[^_\n]+?)_(?!\w)")]
    private static partial Regex InlineToken();

    public static List<MdBlock> Blocks(string md)
    {
        var res = new List<MdBlock>(); var para = new List<string>();
        void Flush() { if (para.Count > 0) { res.Add(new(MdKind.Paragraph, string.Join("\n", para))); para.Clear(); } }
        var lines = md.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i]; var t = l.Trim();
            if (t == "") { Flush(); continue; }
            var h = HeadingLine().Match(l);
            if (h.Success) { Flush(); res.Add(new(MdKind.Heading, h.Groups[2].Value.Trim(), h.Groups[1].Length)); continue; }
            if (RuleLine().IsMatch(t)) { Flush(); res.Add(new(MdKind.Rule, "")); continue; }
            if (t.StartsWith('|') && i + 1 < lines.Length && TableSep().IsMatch(lines[i + 1].Trim()))
            {
                Flush();
                var rows = new List<List<string>> { Cells(t) };
                i += 2;
                while (i < lines.Length && lines[i].Trim().StartsWith('|')) { rows.Add(Cells(lines[i].Trim())); i++; }
                i--;
                res.Add(new(MdKind.Table, "", 0, rows));
                continue;
            }
            var b = BulletLine().Match(l);
            if (b.Success) { Flush(); res.Add(new(MdKind.Bullet, b.Groups[3].Value, b.Groups[1].Length / 2)); continue; }
            var n = NumberLine().Match(l);
            if (n.Success) { Flush(); res.Add(new(MdKind.Number, n.Groups[3].Value, n.Groups[1].Length / 2, null, n.Groups[2].Value)); continue; }
            if (t.StartsWith('>'))
            {
                Flush();
                var q = t.TrimStart('>').Trim();
                if (res.Count > 0 && res[^1].Kind == MdKind.Quote) res[^1] = res[^1] with { Text = res[^1].Text + "\n" + q };
                else res.Add(new(MdKind.Quote, q));
                continue;
            }
            para.Add(l.TrimEnd());
        }
        Flush();
        return res;
    }

    static List<string> Cells(string row) => row.Trim('|').Split('|').Select(c => c.Trim()).ToList();

    public static List<MdSpan> Spans(string text)
    {
        var res = new List<MdSpan>(); int at = 0;
        foreach (Match m in InlineToken().Matches(text))
        {
            if (m.Index > at) res.Add(new(text[at..m.Index], false, false, false, null));
            at = m.Index + m.Length;
            if (m.Groups["code"].Success) res.Add(new(m.Groups["code"].Value, false, false, true, null));
            else if (m.Groups["lt"].Success)
            {
                string lt = m.Groups["lt"].Value, url = m.Groups["url"].Value;
                res.Add(new(lt == url ? lt : $"{lt} ({url})", false, false, false, url));
            }
            else if (m.Groups["b1"].Success || m.Groups["b2"].Success) res.Add(new(m.Groups["b1"].Success ? m.Groups["b1"].Value : m.Groups["b2"].Value, true, false, false, null));
            else res.Add(new(m.Groups["i1"].Success ? m.Groups["i1"].Value : m.Groups["i2"].Value, false, true, false, null));
        }
        if (at < text.Length) res.Add(new(text[at..], false, false, false, null));
        return res;
    }
}
