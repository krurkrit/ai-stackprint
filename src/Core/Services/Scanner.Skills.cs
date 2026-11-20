// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

public static partial class Scanner
{
    public static void ReadRoot(string path, ScanRoot r, string source, List<Item> rows)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            if (r.Layout == "flat-md")
            {
                foreach (var f in Directory.EnumerateFiles(path, "*.md"))
                {
                    rows.Add(ReadSkill(f, source, r.Level));
                }
            }
            else
            {
                Walk(path, r.Depth, source, r.Level, rows);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void Walk(string dir, int depth, string source, string level, List<Item> rows)
    {
        try
        {
            var f = Path.Combine(dir, "SKILL.md");
            if (File.Exists(f))
            {
                rows.Add(ReadSkill(f, source, level));
            }

            if (depth == 0)
            {
                return;
            }

            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                if (!_skip.Contains(Path.GetFileName(d)))
                {
                    Walk(d, depth - 1, source, level, rows);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Reads name, description and invocation flags from the SKILL.md front matter.</summary>
    private static Item ReadSkill(string file, string source, string level)
    {
        var name = Path.GetFileName(file).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(file)!) : Path.GetFileNameWithoutExtension(file);
        string desc = "", block = "";
        string? author = null;
        bool modelOff = false, userOff = false;
        try
        {
            var lines = File.ReadLines(file).Take(40).ToList();
            if (lines.Count > 0 && lines[0].Trim() == "---")
            {
                for (var i = 1; i < lines.Count && lines[i].Trim() != "---"; i++)
                {
                    var l = lines[i];
                    if (l.Length > 0 && !char.IsWhiteSpace(l[0]) && l.Contains(':'))
                    {
                        block = l[..l.IndexOf(':')].Trim();
                    }

                    if (l.StartsWith("name:"))
                    {
                        var n = Clean(l[5..]);
                        if (n != "")
                        {
                            name = n;
                        }
                    }
                    else if (l.StartsWith("author:") || (block == "metadata" && l.Length > 0 && char.IsWhiteSpace(l[0]) && l.TrimStart().StartsWith("author:")))
                    {
                        var a = Clean(l.TrimStart()[7..]);
                        if (a != "")
                        {
                            author = a;
                        }
                    }
                    else if (l.StartsWith("description:"))
                    {
                        desc = Clean(l[12..]);
                        // YAML block scalar (>, |): the text is on the indented lines that follow
                        if (desc is "" or ">" or "|" or ">-" or "|-" or ">+" or "|+")
                        {
                            desc = string.Join(" ", lines.Skip(i + 1).TakeWhile(n => n.Trim() == "" || char.IsWhiteSpace(n[0])).Select(n => n.Trim()));
                        }

                        desc = MarkdownLite.Plain(desc);   // descriptions may mix in HTML and Markdown: shown as one line of text
                    }
                    else if (l.StartsWith("disable-model-invocation:"))
                    {
                        modelOff = Clean(l[25..]).Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (l.StartsWith("user-invocable:"))
                    {
                        userOff = Clean(l[15..]).Equals("false", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        // Agent: the agent may load it; User: you can invoke it (e.g. /name). disable-model-invocation wins when both flags are set.
        string[] invokers = modelOff ? ["User"] : userOff ? ["Agent"] : ["Agent", "User"];
        return new Item("Skill", name, desc, source, level, file)
        {
            Invokers = invokers,
            Author = author
        };
    }

    private static string Clean(string s) => s.Trim().Trim('"', '\'');
}
