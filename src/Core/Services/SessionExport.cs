// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text;

namespace AiStackprint.Services;

/// <summary>A session written as Markdown or HTML (the viewer's Export, and Copy on a step), and the files a session changed. Read-only: the session is not touched.</summary>
public static class SessionExport
{
    /// <summary>The files the session's edit and write calls changed, in the order they were first changed (from the diffs and patches the parsers made).</summary>
    public static List<string> TouchedFiles(IEnumerable<LogEntry> steps)
    {
        var files = new List<string>();
        foreach (var e in steps.Where(e => e.Role == "tool" && e.Detail != null))
        {
            foreach (var line in e.Detail!.Split('\n'))
            {
                var path = line.StartsWith("--- ", StringComparison.Ordinal) && e.Lang == ".diff" ? line[4..]
                    : Patch.FirstOrDefault(p => line.StartsWith(p, StringComparison.Ordinal)) is { } p ? line[p.Length..] : null;
                if (!string.IsNullOrWhiteSpace(path) && !files.Contains(path.Trim()))
                {
                    files.Add(path.Trim());
                }
            }
        }

        return files;
    }

    private static readonly string[] Patch = ["*** Update File: ", "*** Add File: ", "*** Delete File: "];

    /// <summary>One step as Markdown: a heading with who and when, then its text; tool input and results in code fences.</summary>
    public static string Markdown(LogEntry e)
    {
        var sb = new StringBuilder();
        var time = e.Time is { } t ? " · " + t.ToString("yyyy-MM-dd HH:mm:ss") : "";
        switch (e.Role)
        {
            case "user":
                sb.Append("### You").Append(time).Append("\n\n").Append(e.Text.TrimEnd()).Append('\n');
                break;
            case "assistant":
                sb.Append("### Assistant").Append(time).Append("\n\n").Append(e.Text.TrimEnd()).Append('\n');
                break;
            case "tool":
                sb.Append("#### Tool · ").Append(e.Tool ?? "tool").Append(time).Append("\n\n").Append(Fence(e.Detail ?? e.Text, Lang(e.Lang))).Append('\n');
                break;
            case "result":
                sb.Append("#### Result").Append(e.Tool != null ? " · " + e.Tool : "").Append(e.Error ? " · failed" : "").Append("\n\n").Append(Fence(e.Text, Lang(e.Lang))).Append('\n');
                break;
            case "thinking":
                sb.Append("> **Thinking**\n>\n").Append(string.Join('\n', e.Text.TrimEnd().Split('\n').Select(l => "> " + l))).Append('\n');
                break;
            default:
                sb.Append("#### ").Append(e.Role.Length > 0 ? char.ToUpperInvariant(e.Role[0]) + e.Role[1..] : "Note").Append(time).Append("\n\n").Append(Fence(e.Text, null)).Append('\n');
                break;
        }

        if (e.Usage is { } u)
        {
            sb.Append("\n*").Append(u.Model).Append(" · ").Append(u.RequestInput.ToString("N0")).Append(" in (").Append(u.CacheRead.ToString("N0")).Append(" cached) · ").Append(u.Output.ToString("N0")).Append(" out")
                .Append(ModelCatalog.Cost(u) is { } cost ? " · $" + cost.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) : "").Append("*\n");
        }

        return sb.ToString();
    }

    /// <summary>The whole session as a Markdown document.</summary>
    public static string Markdown(Session s, string title)
    {
        var sb = new StringBuilder("# ").Append(title).Append("\n\n").Append(Facts(s)).Append('\n');
        foreach (var e in s.Entries)
        {
            sb.Append(Markdown(e)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>The whole session as a self-contained HTML page (plain text in blocks; no scripts, no links out).</summary>
    public static string Html(Session s, string title)
    {
        var sb = new StringBuilder("<!doctype html>\n<meta charset=\"utf-8\">\n<title>").Append(WebUtility.HtmlEncode(title)).Append("</title>\n")
            .Append("<style>body{font:15px/1.5 system-ui,sans-serif;max-width:60rem;margin:2rem auto;padding:0 1rem}h3,h4{margin:1.5rem 0 .25rem}pre{white-space:pre-wrap;background:#f4f4f4;padding:.5rem;border-radius:6px}.meta{color:#666}</style>\n")
            .Append("<h1>").Append(WebUtility.HtmlEncode(title)).Append("</h1>\n<p class=\"meta\">").Append(WebUtility.HtmlEncode(Facts(s))).Append("</p>\n");
        foreach (var e in s.Entries)
        {
            var who = e.Role switch { "user" => "You", "assistant" => "Assistant", "tool" => "Tool · " + e.Tool, "result" => "Result" + (e.Tool != null ? " · " + e.Tool : ""), "thinking" => "Thinking", _ => e.Role };
            sb.Append("<h4>").Append(WebUtility.HtmlEncode(who)).Append(e.Time is { } t ? " · " + t.ToString("yyyy-MM-dd HH:mm:ss") : "").Append("</h4>\n<pre>")
                .Append(WebUtility.HtmlEncode(e.Role == "tool" ? e.Detail ?? e.Text : e.Text)).Append("</pre>\n");
        }

        return sb.ToString();
    }

    private static string Facts(Session s)
    {
        var parts = new List<string>();
        if (s.Models.Count > 0)
        {
            parts.Add(string.Join(", ", s.Models));
        }

        if (s.Start is { } a)
        {
            parts.Add(a.ToString("yyyy-MM-dd HH:mm") + (s.End is { } b ? " – " + b.ToString("HH:mm") : ""));
        }

        parts.Add($"{s.Entries.Count:N0} steps");
        if (s.InputTokens + s.OutputTokens > 0)
        {
            parts.Add($"{s.InputTokens:N0} in · {s.OutputTokens:N0} out tokens");
        }

        return string.Join(" · ", parts);
    }

    private static string? Lang(string? ext) => ext?.TrimStart('.') is { Length: > 0 } l ? (l == "sh" ? "bash" : l) : null;

    /// <summary>The text in a code fence long enough that no line of it can end the fence early.</summary>
    private static string Fence(string text, string? lang)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var ticks = new string('`', Math.Max(3, longest + 1));
        return ticks + lang + "\n" + text.TrimEnd() + "\n" + ticks;
    }
}
