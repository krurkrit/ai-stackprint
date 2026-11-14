// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

using static AiStackprint.Services.ContextBudget;

namespace AiStackprint.Services;

/// <summary>
/// Gemini CLI (geminicli.com/docs/cli/gemini-md): ~/.gemini/GEMINI.md, then the context files from the project root down to the start folder,
/// @imports; files in sub-folders when a tool reads there. File names from settings.json context.fileName. No size limit is documented.
/// </summary>
internal partial class GeminiContext : IContextRule
{
    public string Id => "gemini";

    public ContextReport Read(string project, string start, string home)
    {
        var gemini = Path.Combine(home, ".gemini");
        var names = Names(ReadText(Path.Combine(gemini, "settings.json")));
        var files = new List<ContextFile>(); var findings = new List<Finding>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, LoadWhen load, string? note = null)
        {
            if (!File.Exists(path) || !seen.Add(Path.GetFullPath(path)))
            {
                return;
            }

            files.Add(Measure(path, load, null, note));
            AddImports(path, home, project, 5, files, findings, seen);
        }
        foreach (var n in names)
        {
            Add(Path.Combine(gemini, n), LoadWhen.Always, "Global");
        }

        foreach (var dir in Chain(RootOf(start, [".git"]) ?? start, start))
        {
            foreach (var n in names)
            {
                Add(Path.Combine(dir, n), LoadWhen.Always);
            }
        }

        foreach (var f in Below(start, names))
        {
            Add(f, LoadWhen.OnDemand, "When a tool reads that folder");
        }

        return new ContextReport(Id, "characters", null, null, files, findings);
    }

    /// <summary>context.fileName: a string or a list; GEMINI.md when not set.</summary>
    private static List<string> Names(string settings)
    {
        var m = FileNameSetting().Match(settings);
        var list = m.Success ? Quoted().Matches(m.Groups[1].Value).Select(x => x.Groups[1].Value).ToList() : [];
        return list.Count > 0 ? list : ["GEMINI.md"];
    }

    [GeneratedRegex("\"fileName\"\\s*:\\s*(\\[[^\\]]*\\]|\"[^\"]*\")")]
    private static partial Regex FileNameSetting();
    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex Quoted();
}

/// <summary>GitHub Copilot: .github/copilot-instructions.md and the nearest AGENTS.md always; .github/instructions/*.instructions.md by their applyTo pattern.</summary>
internal class CopilotContext : IContextRule
{
    public string Id => "copilot";

    public ContextReport Read(string project, string start, string home)
    {
        var files = new List<ContextFile>();
        var main = Path.Combine(project, ".github", "copilot-instructions.md");
        if (File.Exists(main))
        {
            files.Add(Measure(main, LoadWhen.Always, null, "Repository-wide"));
        }

        if (Chain(project, start).AsEnumerable().Reverse().Select(d => Path.Combine(d, "AGENTS.md")).FirstOrDefault(File.Exists) is { } agents)
        {
            files.Add(Measure(agents, LoadWhen.Always, null, "Nearest AGENTS.md"));
        }

        foreach (var f in FilesIn(Path.Combine(project, ".github", "instructions"), "*.instructions.md"))
        {
            var apply = SkillDoc.Parse(ReadText(f)).Get("applyTo");
            files.Add(Measure(f, apply == "" ? LoadWhen.AgentDecides : LoadWhen.Conditional, null, apply == "" ? "No applyTo: used when relevant" : "For " + apply));
        }
        return new ContextReport(Id, "characters", null, null, files, []);
    }
}

/// <summary>
/// Cursor (cursor.com/docs/rules): .cursor/rules/*.mdc by alwaysApply / globs / description, AGENTS.md, legacy .cursorrules.
/// A plain .md in .cursor/rules is ignored. Rules should stay under 500 lines.
/// </summary>
internal class CursorContext : IContextRule
{
    public string Id => "cursor";
    public const int RuleLines = 500;

    public ContextReport Read(string project, string start, string home)
    {
        var files = new List<ContextFile>(); var findings = new List<Finding>();
        var rules = Path.Combine(project, ".cursor", "rules");
        foreach (var f in FilesIn(rules, "*.mdc"))
        {
            var doc = SkillDoc.Parse(ReadText(f));
            string globs = doc.Get("globs"), desc = doc.Get("description");
            var (load, note) = doc.Get("alwaysApply").Equals("true", StringComparison.OrdinalIgnoreCase) ? (LoadWhen.Always, "alwaysApply")
                : globs != "" ? (LoadWhen.Conditional, "For " + globs)
                : desc != "" ? (LoadWhen.AgentDecides, "The agent decides from its description")
                : (LoadWhen.Manual, "Only when you @mention it");
            files.Add(Measure(f, load, null, note));
            if (doc.Lines > RuleLines)
            {
                findings.Add(new($"{Path.GetFileName(f)} has {doc.Lines:N0} lines; Cursor suggests under {RuleLines}.", false));
            }
        }
        foreach (var f in FilesIn(rules, "*.md"))
        {
            files.Add(Measure(f, LoadWhen.Ignored, null, "Ignored: rules need the .mdc extension"));
            findings.Add(new($"{Path.GetFileName(f)} in .cursor/rules is ignored: rules need the .mdc extension (or move it to AGENTS.md)."));
        }
        var agents = Path.Combine(project, "AGENTS.md");
        if (File.Exists(agents))
        {
            files.Add(Measure(agents, LoadWhen.Always));
        }

        var legacy = Path.Combine(project, ".cursorrules");
        if (File.Exists(legacy)) { files.Add(Measure(legacy, LoadWhen.Always, null, "Deprecated: move to .cursor/rules")); findings.Add(new(".cursorrules is deprecated; move it into .cursor/rules as .mdc files.", false)); }
        return new ContextReport(Id, "characters", null, null, files, findings);
    }
}
