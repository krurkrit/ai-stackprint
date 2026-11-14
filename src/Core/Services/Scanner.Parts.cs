// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Enumeration;
using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

public static partial class Scanner
{
    private static readonly string[] _partEndings = [".prompt.md", ".agent.md", ".chatmode.md", ".md", ".toml"];

    /// <summary>
    /// Commands and agents: one "Command" or "Agent" item per matching file under <paramref name="path"/>. The name is the file name without its
    /// ending (.prompt.md, .agent.md, .md, .toml); sub-folders prefix it (team/review.md is team:review). The description and an agent's name
    /// come from the front matter, or description = "…" in a .toml file.
    /// </summary>
    public static void ReadParts(string path, PartSource s, string source, List<Item> rows)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var patterns = s.Pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var options = new EnumerationOptions { RecurseSubdirectories = s.Depth > 0, MaxRecursionDepth = s.Depth, IgnoreInaccessible = true };
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", options).Where(f => patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, Path.GetFileName(f)))))
            {
                rows.Add(ReadPart(path, f, s, source));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static Item ReadPart(string root, string file, PartSource s, string source)
    {
        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        var ending = _partEndings.FirstOrDefault(e => rel.EndsWith(e, StringComparison.OrdinalIgnoreCase)) ?? Path.GetExtension(rel);
        var name = rel[..^ending.Length].Replace('/', ':');
        string desc;
        if (file.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
        {
            desc = TomlDescription().Match(ContextBudget.ReadText(file)) is { Success: true } m ? m.Groups[2].Value : "";
        }
        else
        {
            var doc = SkillDoc.Parse(ContextBudget.ReadText(file));
            desc = doc.Get("description");
            if (s.Kind == "agent" && doc.Get("name") is { Length: > 0 } n)
            {
                name = n;
            }
        }

        return new Item(s.Kind == "agent" ? "Agent" : "Command", name, MarkdownLite.Plain(desc), source, s.Level, file) { LevelLabel = s.Label };
    }

    [GeneratedRegex(@"(?m)^\s*description\s*=\s*([""'])(.*?)\1\s*$")] private static partial Regex TomlDescription();
}
