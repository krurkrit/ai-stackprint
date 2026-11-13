using System.Text.RegularExpressions;
using static AiStackprint.Services.ContextBudget;

namespace AiStackprint.Services;

/// <summary>
/// Claude Code (code.claude.com/docs/en/memory): CLAUDE.md, .claude/CLAUDE.md and CLAUDE.local.md from the start folder and every folder above it,
/// the user file and rules, managed policy, @imports (four hops), auto memory (first 200 lines or 25 KB) at launch; CLAUDE.md in sub-folders on demand;
/// rules with paths: front matter only for matching files. Warns above 40,000 characters.
/// </summary>
class ClaudeContext : IContextRule
{
    public string Id => "claude";
    public const int WarnChars = 40_000, MaxHops = 4, MemoryLines = 200, MemoryBytes = 25 * 1024;

    public ContextReport Read(string project, string start, string home)
    {
        var files = new List<ContextFile>(); var findings = new List<Finding>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, LoadWhen load, string? note = null)
        {
            if (!File.Exists(path) || !seen.Add(Path.GetFullPath(path))) return;
            files.Add(Measure(path, load, null, note));
            if (load is LoadWhen.Always or LoadWhen.OnDemand) AddImports(path, home, project, MaxHops, files, findings, seen);
        }
        foreach (var m in new[] { @"C:\Program Files\ClaudeCode\CLAUDE.md", "/Library/Application Support/ClaudeCode/CLAUDE.md", "/etc/claude-code/CLAUDE.md" })
            if (PathOk(m)) Add(m, LoadWhen.Always, "Managed policy");
        var claudeHome = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c : Path.Combine(home, ".claude");
        Add(Path.Combine(claudeHome, "CLAUDE.md"), LoadWhen.Always, "User");
        Rules(Path.Combine(claudeHome, "rules"), "User rule");
        foreach (var dir in Ancestors(start))
        {
            Add(Path.Combine(dir, "CLAUDE.md"), LoadWhen.Always);
            Add(Path.Combine(dir, ".claude", "CLAUDE.md"), LoadWhen.Always);
            Add(Path.Combine(dir, "CLAUDE.local.md"), LoadWhen.Always, "Local (personal)");
        }
        Rules(Path.Combine(project, ".claude", "rules"), "Project rule");
        Memory(Path.Combine(claudeHome, "projects", Regex.Replace(start, "[^A-Za-z0-9]", "-"), "memory", "MEMORY.md"));
        foreach (var f in Below(start, new[] { "CLAUDE.md", "CLAUDE.local.md" })) Add(f, LoadWhen.OnDemand, "When Claude reads files in that folder");

        foreach (var f in files.Where(f => f.Chars > WarnChars))
            findings.Add(new($"{Path.GetFileName(f.Path)} is {f.Chars:N0} characters; Claude Code warns above {WarnChars:N0} (\"Large CLAUDE.md will impact performance\")."));
        var report = new ContextReport(Id, "characters", WarnChars, "Claude Code warns when its instruction files pass 40,000 characters. Imports still load at launch.", files, findings);
        if (report.AlwaysTotal > WarnChars && !files.Any(f => f.Chars > WarnChars))
            findings.Add(new($"Instructions loaded at launch total {report.AlwaysTotal:N0} characters, over the {WarnChars:N0} Claude Code warns about.", false));
        return report;

        void Rules(string dir, string note)
        {
            foreach (var f in FilesIn(dir, "*.md"))
            {
                var paths = SkillDoc.Parse(ReadText(f)).Get("paths");
                Add(f, paths == "" ? LoadWhen.Always : LoadWhen.Conditional, paths == "" ? note : $"{note} · for {paths}");
            }
        }

        void Memory(string path)
        {
            if (!File.Exists(path)) return;
            var full = Measure(path, LoadWhen.Always, null, $"Auto memory: first {MemoryLines} lines or 25 KB");
            var kept = string.Join('\n', ReadText(path).Split('\n').Take(MemoryLines));
            files.Add(full with { Kept = Math.Min(Math.Min(kept.Length, full.Chars), MemoryBytes) });
        }
    }

    static bool PathOk(string p) => Path.IsPathRooted(p) && (OperatingSystem.IsWindows() ? p.Contains(':') : !p.Contains(':'));
}

/// <summary>
/// Codex (developers.openai.com/codex/guides/agents-md): the global AGENTS.override.md or AGENTS.md, then one file per folder from the project root
/// (nearest .git) down to the start folder: AGENTS.override.md, AGENTS.md, then project_doc_fallback_filenames. Empty files are skipped.
/// Project files stop at project_doc_max_bytes (32 KiB by default) without a warning.
/// </summary>
class CodexContext : IContextRule
{
    public string Id => "codex";
    public const int DefaultMaxBytes = 32 * 1024;

    public ContextReport Read(string project, string start, string home)
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".codex");
        var config = ReadText(Path.Combine(codexHome, "config.toml"));
        long max = Regex.Match(config, @"(?m)^\s*project_doc_max_bytes\s*=\s*(\d+)") is { Success: true } m && long.TryParse(m.Groups[1].Value, out var n) ? n : DefaultMaxBytes;
        var fallbacks = Regex.Match(config, @"(?m)^\s*project_doc_fallback_filenames\s*=\s*\[(.*?)\]") is { Success: true } f
            ? Regex.Matches(f.Groups[1].Value, "\"([^\"]+)\"").Select(x => x.Groups[1].Value).ToList() : new List<string>();
        var files = new List<ContextFile>(); var findings = new List<Finding>();

        var global = new[] { "AGENTS.override.md", "AGENTS.md" }.Select(n => Path.Combine(codexHome, n)).FirstOrDefault(p => File.Exists(p) && new FileInfo(p).Length > 0);
        if (global != null) files.Add(Measure(global, LoadWhen.Always, null, "Global · not counted in project_doc_max_bytes") with { Counted = false });

        var root = RootOf(start, new[] { ".git" }) ?? start;
        long used = 0;
        foreach (var dir in Chain(root, start))
        {
            var pick = new[] { "AGENTS.override.md", "AGENTS.md" }.Concat(fallbacks).Select(n => Path.Combine(dir, n)).FirstOrDefault(p => File.Exists(p) && new FileInfo(p).Length > 0);
            if (pick == null) continue;
            var cf = Measure(pick, LoadWhen.Always, null, Path.GetFileName(pick) == "AGENTS.override.md" ? "Override: AGENTS.md here is skipped" : null);
            long left = Math.Max(0, max - used);
            if (cf.Bytes > left)
            {
                cf = cf with { Kept = left, Note = left == 0 ? "Not loaded: over project_doc_max_bytes" : $"Cut after {left:N0} bytes" };
                findings.Add(new(left == 0 ? $"{Rel(pick, root)} is not loaded: the files before it already use the {max:N0}-byte limit (project_doc_max_bytes)."
                    : $"{Rel(pick, root)} is cut after {left:N0} of {cf.Bytes:N0} bytes: Codex stops at {max:N0} bytes (project_doc_max_bytes) without a warning."));
            }
            used += cf.Kept ?? cf.Bytes;
            files.Add(cf);
        }
        return new ContextReport(Id, "bytes", max, $"project_doc_max_bytes = {max:N0} (config.toml); the global AGENTS.md is not counted.", files, findings);
    }

    static string Rel(string p, string root) => Path.GetRelativePath(root, p);
}

/// <summary>
/// Gemini CLI (geminicli.com/docs/cli/gemini-md): ~/.gemini/GEMINI.md, then the context files from the project root down to the start folder,
/// @imports; files in sub-folders when a tool reads there. File names from settings.json context.fileName. No size limit is documented.
/// </summary>
class GeminiContext : IContextRule
{
    public string Id => "gemini";

    public ContextReport Read(string project, string start, string home)
    {
        var gemini = Path.Combine(home, ".gemini");
        var names = Names(ReadText(Path.Combine(gemini, "settings.json")));
        var files = new List<ContextFile>(); var findings = new List<Finding>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, LoadWhen load, string? note = null)
        {
            if (!File.Exists(path) || !seen.Add(Path.GetFullPath(path))) return;
            files.Add(Measure(path, load, null, note));
            AddImports(path, home, project, 5, files, findings, seen);
        }
        foreach (var n in names) Add(Path.Combine(gemini, n), LoadWhen.Always, "Global");
        foreach (var dir in Chain(RootOf(start, new[] { ".git" }) ?? start, start))
            foreach (var n in names) Add(Path.Combine(dir, n), LoadWhen.Always);
        foreach (var f in Below(start, names)) Add(f, LoadWhen.OnDemand, "When a tool reads that folder");
        return new ContextReport(Id, "characters", null, null, files, findings);
    }

    /// <summary>context.fileName: a string or a list; GEMINI.md when not set.</summary>
    static List<string> Names(string settings)
    {
        var m = Regex.Match(settings, "\"fileName\"\\s*:\\s*(\\[[^\\]]*\\]|\"[^\"]*\")");
        var list = m.Success ? Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"").Select(x => x.Groups[1].Value).ToList() : new();
        return list.Count > 0 ? list : new() { "GEMINI.md" };
    }
}

/// <summary>GitHub Copilot: .github/copilot-instructions.md and the nearest AGENTS.md always; .github/instructions/*.instructions.md by their applyTo pattern.</summary>
class CopilotContext : IContextRule
{
    public string Id => "copilot";

    public ContextReport Read(string project, string start, string home)
    {
        var files = new List<ContextFile>();
        var main = Path.Combine(project, ".github", "copilot-instructions.md");
        if (File.Exists(main)) files.Add(Measure(main, LoadWhen.Always, null, "Repository-wide"));
        if (Chain(project, start).AsEnumerable().Reverse().Select(d => Path.Combine(d, "AGENTS.md")).FirstOrDefault(File.Exists) is { } agents)
            files.Add(Measure(agents, LoadWhen.Always, null, "Nearest AGENTS.md"));
        foreach (var f in FilesIn(Path.Combine(project, ".github", "instructions"), "*.instructions.md"))
        {
            var apply = SkillDoc.Parse(ReadText(f)).Get("applyTo");
            files.Add(Measure(f, apply == "" ? LoadWhen.AgentDecides : LoadWhen.Conditional, null, apply == "" ? "No applyTo: used when relevant" : "For " + apply));
        }
        return new ContextReport(Id, "characters", null, null, files, new());
    }
}

/// <summary>
/// Cursor (cursor.com/docs/rules): .cursor/rules/*.mdc by alwaysApply / globs / description, AGENTS.md, legacy .cursorrules.
/// A plain .md in .cursor/rules is ignored. Rules should stay under 500 lines.
/// </summary>
class CursorContext : IContextRule
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
            if (doc.Lines > RuleLines) findings.Add(new($"{Path.GetFileName(f)} has {doc.Lines:N0} lines; Cursor suggests under {RuleLines}.", false));
        }
        foreach (var f in FilesIn(rules, "*.md"))
        {
            files.Add(Measure(f, LoadWhen.Ignored, null, "Ignored: rules need the .mdc extension"));
            findings.Add(new($"{Path.GetFileName(f)} in .cursor/rules is ignored: rules need the .mdc extension (or move it to AGENTS.md)."));
        }
        var agents = Path.Combine(project, "AGENTS.md");
        if (File.Exists(agents)) files.Add(Measure(agents, LoadWhen.Always));
        var legacy = Path.Combine(project, ".cursorrules");
        if (File.Exists(legacy)) { files.Add(Measure(legacy, LoadWhen.Always, null, "Deprecated: move to .cursor/rules")); findings.Add(new(".cursorrules is deprecated; move it into .cursor/rules as .mdc files.", false)); }
        return new ContextReport(Id, "characters", null, null, files, findings);
    }
}
