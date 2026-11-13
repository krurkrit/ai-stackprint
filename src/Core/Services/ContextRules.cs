// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

using static AiStackprint.Services.ContextBudget;

namespace AiStackprint.Services;

/// <summary>
/// Claude Code (code.claude.com/docs/en/memory): CLAUDE.md, .claude/CLAUDE.md and CLAUDE.local.md from the start folder and every folder above it,
/// the user file and rules, managed policy, @imports (four hops), auto memory (first 200 lines or 25 KB) at launch; CLAUDE.md in sub-folders on demand;
/// rules with paths: front matter only for matching files. Warns above 40,000 characters.
/// </summary>
internal class ClaudeContext : IContextRule
{
    public string Id => "claude";
    public const int WarnChars = 40_000, MaxHops = 4, MemoryLines = 200, MemoryBytes = 25 * 1024;

    public ContextReport Read(string project, string start, string home)
    {
        var files = new List<ContextFile>(); var findings = new List<Finding>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, LoadWhen load, string? note = null)
        {
            if (!File.Exists(path) || !seen.Add(Path.GetFullPath(path)))
            {
                return;
            }

            files.Add(Measure(path, load, null, note));
            if (load is LoadWhen.Always or LoadWhen.OnDemand)
            {
                AddImports(path, home, project, MaxHops, files, findings, seen);
            }
        }
        foreach (var m in new[] { @"C:\Program Files\ClaudeCode\CLAUDE.md", "/Library/Application Support/ClaudeCode/CLAUDE.md", "/etc/claude-code/CLAUDE.md" })
        {
            if (PathOk(m))
            {
                Add(m, LoadWhen.Always, "Managed policy");
            }
        }

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
        foreach (var f in Below(start, ["CLAUDE.md", "CLAUDE.local.md"]))
        {
            Add(f, LoadWhen.OnDemand, "When Claude reads files in that folder");
        }

        foreach (var f in files.Where(f => f.Chars > WarnChars))
        {
            findings.Add(new($"{Path.GetFileName(f.Path)} is {f.Chars:N0} characters; Claude Code warns above {WarnChars:N0} (\"Large CLAUDE.md will impact performance\")."));
        }

        var report = new ContextReport(Id, "characters", WarnChars, "Claude Code warns when its instruction files pass 40,000 characters. Imports still load at launch.", files, findings);
        if (report.AlwaysTotal > WarnChars && !files.Any(f => f.Chars > WarnChars))
        {
            findings.Add(new($"Instructions loaded at launch total {report.AlwaysTotal:N0} characters, over the {WarnChars:N0} Claude Code warns about.", false));
        }

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
            if (!File.Exists(path))
            {
                return;
            }

            var full = Measure(path, LoadWhen.Always, null, $"Auto memory: first {MemoryLines} lines or 25 KB");
            var kept = string.Join('\n', ReadText(path).Split('\n').Take(MemoryLines));
            files.Add(full with { Kept = Math.Min(Math.Min(kept.Length, full.Chars), MemoryBytes) });
        }
    }

    private static bool PathOk(string p) => Path.IsPathRooted(p) && (OperatingSystem.IsWindows() ? p.Contains(':') : !p.Contains(':'));
}

/// <summary>
/// Codex (developers.openai.com/codex/guides/agents-md): the global AGENTS.override.md or AGENTS.md, then one file per folder from the project root
/// (nearest .git) down to the start folder: AGENTS.override.md, AGENTS.md, then project_doc_fallback_filenames. Empty files are skipped.
/// Project files stop at project_doc_max_bytes (32 KiB by default) without a warning.
/// </summary>
internal class CodexContext : IContextRule
{
    public string Id => "codex";
    public const int DefaultMaxBytes = 32 * 1024;

    public ContextReport Read(string project, string start, string home)
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".codex");
        var config = ReadText(Path.Combine(codexHome, "config.toml"));
        var max = Regex.Match(config, @"(?m)^\s*project_doc_max_bytes\s*=\s*(\d+)") is { Success: true } m && long.TryParse(m.Groups[1].Value, out var n) ? n : DefaultMaxBytes;
        var fallbacks = Regex.Match(config, @"(?m)^\s*project_doc_fallback_filenames\s*=\s*\[(.*?)\]") is { Success: true } f
            ? Regex.Matches(f.Groups[1].Value, "\"([^\"]+)\"").Select(x => x.Groups[1].Value).ToList() : [];
        var files = new List<ContextFile>(); var findings = new List<Finding>();

        var global = new[] { "AGENTS.override.md", "AGENTS.md" }.Select(n => Path.Combine(codexHome, n)).FirstOrDefault(p => File.Exists(p) && new FileInfo(p).Length > 0);
        if (global != null)
        {
            files.Add(Measure(global, LoadWhen.Always, null, "Global · not counted in project_doc_max_bytes") with { Counted = false });
        }

        var root = RootOf(start, [".git"]) ?? start;
        long used = 0;
        foreach (var dir in Chain(root, start))
        {
            var pick = new[] { "AGENTS.override.md", "AGENTS.md" }.Concat(fallbacks).Select(n => Path.Combine(dir, n)).FirstOrDefault(p => File.Exists(p) && new FileInfo(p).Length > 0);
            if (pick == null)
            {
                continue;
            }

            var cf = Measure(pick, LoadWhen.Always, null, Path.GetFileName(pick) == "AGENTS.override.md" ? "Override: AGENTS.md here is skipped" : null);
            var left = Math.Max(0, max - used);
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

    private static string Rel(string p, string root) => Path.GetRelativePath(root, p);
}
