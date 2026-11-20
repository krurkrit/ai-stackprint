// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <param name="Warning">false = a guideline (information), true = likely broken or cut off.</param>
public record Finding(string Text, bool Warning = true);

/// <summary>
/// Checks a skill against the Agent Skills format (agentskills.io/specification): name and description rules, size guideline,
/// links that are broken or leave the skill folder, and references nested more than one level. Read-only.
/// </summary>
public static partial class SkillCheck
{
    internal const int NameMax = 64, DescriptionMax = 1024, CompatibilityMax = 500, BodyLines = 500;
    /// <summary>Claude Code: each description is cut at this many characters in the skill list it keeps in context.</summary>
    internal const int ClaudeDescriptionCap = 1536;
    /// <summary>Claude Code: the skill list's budget is 1% of the context window; this is the documented fallback.</summary>
    internal const int ClaudeListingFallback = 8000;

    /// <summary>
    /// The skill list's budget in characters: 1% of the context window at about 4 characters per token (the fallback is the same 1% of a
    /// 200K window), or the fallback when the window is unknown.
    /// </summary>
    public static long ClaudeListingBudget(long? window) => window is > 0 ? window.Value / 100 * 4 : ClaudeListingFallback;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")] private static partial Regex NameRule();

    public static List<Finding> Check(string path, SkillDoc doc)
    {
        var f = new List<Finding>();
        var skillMd = Path.GetFileName(path).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase);
        var root = Path.GetDirectoryName(path) ?? "";
        if (skillMd)
        {
            if (!doc.HasFront)
            {
                f.Add(new("No front matter (--- … ---): name and description are missing."));
            }
            else if (!doc.FrontClosed)
            {
                f.Add(new("The front matter is not closed with a --- line."));
            }
            else
            {
                var name = doc.Get("name");
                var folder = Path.GetFileName(root);
                if (name == "")
                {
                    f.Add(new("No name in the front matter."));
                }
                else
                {
                    if (name.Length > NameMax)
                    {
                        f.Add(new($"Name is {name.Length} characters (limit {NameMax})."));
                    }

                    if (!NameRule().IsMatch(name))
                    {
                        f.Add(new("Name should use only lowercase letters, numbers and single hyphens, not at the start or end."));
                    }

                    if (!name.Equals(folder, StringComparison.Ordinal))
                    {
                        f.Add(new($"Name \"{name}\" differs from its folder \"{folder}\".", false));
                    }
                }
                var desc = doc.Get("description");
                if (desc == "")
                {
                    f.Add(new("No description: the agent cannot tell when to use it."));
                }
                else if (desc.Length > DescriptionMax)
                {
                    f.Add(new($"Description is {desc.Length:N0} characters (limit {DescriptionMax:N0})."));
                }

                if (doc.Get("compatibility").Length > CompatibilityMax)
                {
                    f.Add(new($"Compatibility is over {CompatibilityMax} characters."));
                }
            }
            if (doc.Lines > BodyLines)
            {
                f.Add(new($"SKILL.md has {doc.Lines:N0} lines; keep it under {BodyLines} and move detail into reference files.", false));
            }
        }
        f.AddRange(LinkFindings(root, path, doc.Body, nested: skillMd));
        return f;
    }

    /// <summary>Broken links, links that leave the folder and (from SKILL.md) references that link on to further files.</summary>
    private static IEnumerable<Finding> LinkFindings(string root, string file, string md, bool nested)
    {
        foreach (var link in SkillDoc.Links(md))
        {
            if (SkillDoc.IsExternal(link) || link.StartsWith('#'))
            {
                continue;
            }

            var full = SkillDoc.Resolve(root, file, link);
            if (full == null)
            {
                yield return new($"Link leaves the skill folder: {link}");
                continue;
            }
            if (!File.Exists(full) && !Directory.Exists(full))
            {
                yield return new($"Broken link: {link}");
                continue;
            }
            if (!nested || !full.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            List<string> further;
            try { further = [.. SkillDoc.Links(SkillDoc.Read(full).Body).Where(l => SkillDoc.Resolve(root, full, l) is { } p && p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))]; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (further.Count > 0)
            {
                yield return new($"{link} links on to {string.Join(", ", further.Take(3))}; keep references one level deep from SKILL.md.", false);
            }
        }
    }

    /// <summary>Characters Claude Code keeps in context to list these skills (name + description, each capped).</summary>
    public static int ClaudeListing(IEnumerable<(string Name, string Description)> skills) =>
        skills.Sum(s => Math.Min(ClaudeDescriptionCap, s.Description.Length) + s.Name.Length + 4);
}
