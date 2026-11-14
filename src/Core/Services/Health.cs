// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <param name="Area">Skills, Plugins or Projects.</param>
/// <param name="Subject">The skill, plugin, app or project it is about.</param>
public record HealthIssue(string Area, string Subject, string App, Finding Finding, Item? Item = null)
{
    /// <summary>The project folder, for issues of the Projects area.</summary>
    public string? Project { get; init; }
}

/// <summary>
/// Checks over the whole scan: each skill against the Agent Skills format (<see cref="SkillCheck"/>), the size of Claude Code's skill list,
/// skills with the same name at more than one level, plugins whose folder is missing and plugins that run programs. Read-only.
/// </summary>
public static class Health
{
    public static List<HealthIssue> Compute(IReadOnlyList<Item> items)
    {
        var res = new List<HealthIssue>();
        foreach (var s in items.Where(i => i.Kind == "Skill"))
        {
            SkillDoc doc;
            try { doc = SkillDoc.Read(s.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { res.Add(new("Skills", s.Name, s.App, new("Cannot read: " + e.Message), s)); continue; }
            foreach (var f in SkillCheck.Check(s.Path, doc))
            {
                res.Add(new("Skills", s.Name, s.App, f, s));
            }
        }
        foreach (var g in items.Where(i => i.Kind == "Skill").GroupBy(i => (i.App, i.Name)).Where(g => g.Select(i => i.Level).Distinct().Count() > 1))
        {
            var levels = g.Select(i => i.Level).Distinct().OrderBy(Rank).ToList();
            res.Add(new("Skills", g.Key.Name, g.Key.App, new($"The same name is at {string.Join(", ", levels)} level; {levels[0]} is listed first.", false), g.OrderBy(i => Rank(i.Level)).First()));
        }

        // Claude Code keeps names and descriptions of the skills the agent may load in context, within a budget (1% of the window, 8,000 characters when it cannot tell)
        var listed = items.Where(i => i.Kind == "Skill" && i.App == "claude-code" && i.Invokers.Contains("Agent")).DistinctBy(i => i.Name).ToList();
        var chars = SkillCheck.ClaudeListing(listed.Select(i => (i.Name, i.Description)));
        if (chars > SkillCheck.ClaudeListingFallback)
        {
            res.Add(new("Skills", "Claude Code skill list", "claude-code", new($"{listed.Count} skills the agent can load use about {chars:N0} characters of names and descriptions. " +
                $"Claude Code keeps about 1% of the context window for this list ({SkillCheck.ClaudeListingFallback:N0} characters when it cannot tell) and drops descriptions of the least-used skills first. " +
                "Set rarely used skills to name-only (skillOverrides) or raise skillListingBudgetFraction.")));
        }

        foreach (var p in items.Where(i => i.Kind == "Plugin"))
        {
            if (p.Path == "" || !Directory.Exists(p.Path)) { res.Add(new("Plugins", p.Name, p.App, new("Listed as installed, but its folder is missing: " + (p.Path == "" ? "(no path)" : p.Path)), p)); continue; }
            if (File.Exists(Path.Combine(p.Path, "hooks", "hooks.json")))
            {
                res.Add(new("Plugins", p.Name, p.App, new("Has hooks: runs commands on this computer when the app does certain things.", false), p));
            }

            if (File.Exists(Path.Combine(p.Path, ".mcp.json")))
            {
                res.Add(new("Plugins", p.Name, p.App, new("Starts MCP servers: programs that run on this computer.", false), p));
            }
        }
        foreach (var g in items.Where(i => i.Kind == "Plugin").GroupBy(i => (i.App, i.Name)).Where(g => g.Count() > 1))
        {
            res.Add(new("Plugins", g.Key.Name, g.Key.App, new($"Installed in {g.Count()} scopes: {string.Join(", ", g.Select(i => i.Scope ?? i.Level))}.", false), g.First()));
        }

        return res;
    }

    private static int Rank(string level) { var i = Array.IndexOf(["System", "Local", "Project", "User"], level); return i < 0 ? 9 : i; }
}
