// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using static AiStackprint.Services.ContextBudget;

namespace AiStackprint.Services;

/// <summary>
/// Windsurf (now Devin Desktop; docs.devin.ai/desktop/cascade/memories): global_rules.md always; workspace rules in .devin/rules and
/// .windsurf/rules by their trigger (always_on, glob with globs, model_decision, manual); AGENTS.md, AGENTS.local.md and AGENT.md as always-on
/// rules in the workspace root and each folder down to where the agent works. No size limit is documented now.
/// </summary>
internal sealed class WindsurfContext : IContextRule
{
    public string Id => "windsurf";

    public ContextReport Read(string project, string start, string home)
    {
        var files = new List<ContextFile>();
        var global = Path.Combine(home, ".codeium", "windsurf", "memories", "global_rules.md");
        if (File.Exists(global))
        {
            files.Add(Measure(global, LoadWhen.Always, null, "Global rules"));
        }

        foreach (var dir in new[] { ".devin/rules", ".windsurf/rules" })
        {
            foreach (var f in FilesIn(Path.Combine(project, dir.Replace('/', Path.DirectorySeparatorChar)), "*.md"))
            {
                var doc = SkillDoc.Parse(ReadText(f));
                var globs = doc.Get("globs");
                var (load, note) = doc.Get("trigger").ToLowerInvariant() switch
                {
                    "always_on" => (LoadWhen.Always, "always_on"),
                    "glob" => (LoadWhen.Conditional, "For " + globs),
                    "model_decision" => (LoadWhen.AgentDecides, "The agent decides from its description"),
                    "manual" => (LoadWhen.Manual, "Only when you @mention it"),
                    _ => (LoadWhen.Manual, "No trigger in its front matter"),
                };
                files.Add(Measure(f, load, null, note) with { Patterns = load == LoadWhen.Conditional && globs != "" ? Glob.Split(globs) : null });
            }
        }

        foreach (var dir in Chain(project, start))
        {
            foreach (var n in new[] { "AGENTS.md", "AGENTS.local.md", "AGENT.md" })
            {
                var f = Path.Combine(dir, n);
                if (File.Exists(f))
                {
                    files.Add(Measure(f, LoadWhen.Always, null, "Always-on rule"));
                }
            }
        }

        return new ContextReport(Id, "characters", null, null, files, []);
    }
}

/// <summary>
/// Kiro (kiro.dev/docs/steering): steering files in ~/.kiro/steering and .kiro/steering by inclusion: always (the default), fileMatch with
/// fileMatchPattern, manual (#name in chat). AGENTS.md is always included. No size limit is documented.
/// </summary>
internal sealed class KiroContext : IContextRule
{
    public string Id => "kiro";

    public ContextReport Read(string project, string start, string home)
    {
        var files = new List<ContextFile>();
        Steering(Path.Combine(home, ".kiro", "steering"), "Global steering");
        Steering(Path.Combine(project, ".kiro", "steering"), "Workspace steering");
        var agents = Path.Combine(project, "AGENTS.md");
        if (File.Exists(agents))
        {
            files.Add(Measure(agents, LoadWhen.Always, null, "AGENTS.md is always included"));
        }

        return new ContextReport(Id, "characters", null, null, files, []);

        void Steering(string dir, string where)
        {
            foreach (var f in FilesIn(dir, "*.md"))
            {
                var doc = SkillDoc.Parse(ReadText(f));
                var pattern = doc.Get("fileMatchPattern");
                var (load, note) = doc.Get("inclusion").ToLowerInvariant() switch
                {
                    "" or "always" => (LoadWhen.Always, where),
                    "filematch" => (LoadWhen.Conditional, $"{where} · for {pattern}"),
                    "manual" => (LoadWhen.Manual, $"{where} · when referenced with #{Path.GetFileNameWithoutExtension(f)}"),
                    "auto" => (LoadWhen.AgentDecides, $"{where} · the agent decides from its description"),
                    var other => (LoadWhen.Manual, $"{where} · unknown inclusion: {other}"),
                };
                files.Add(Measure(f, load, null, note) with { Patterns = load == LoadWhen.Conditional && pattern != "" ? Glob.Split(pattern) : null });
            }
        }
    }
}

/// <summary>
/// Qwen Code (qwenlm.github.io/qwen-code-docs: settings, memory): the context file (QWEN.md, or context.fileName in ~/.qwen/settings.json) in
/// ~/.qwen, then in each folder from the project root (.git) down to the start folder; .qwen/QWEN.local.md (personal); AGENTS.md too.
/// No size limit is documented.
/// </summary>
internal sealed class QwenContext : IContextRule
{
    public string Id => "qwen";

    public ContextReport Read(string project, string start, string home)
    {
        var qwen = Environment.GetEnvironmentVariable("QWEN_HOME") is { Length: > 0 } q ? q : Path.Combine(home, ".qwen");
        var names = GeminiContext.Names(ReadText(Path.Combine(qwen, "settings.json")), "QWEN.md");
        var files = new List<ContextFile>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, string? note = null)
        {
            if (File.Exists(path) && seen.Add(Path.GetFullPath(path)))
            {
                files.Add(Measure(path, LoadWhen.Always, null, note));
            }
        }

        foreach (var n in names)
        {
            Add(Path.Combine(qwen, n), "Global");
        }

        foreach (var dir in Chain(RootOf(start, [".git"]) ?? start, start))
        {
            foreach (var n in names)
            {
                Add(Path.Combine(dir, n));
            }
        }

        Add(Path.Combine(project, ".qwen", "QWEN.local.md"), "Local (personal)");
        Add(Path.Combine(project, "AGENTS.md"), "Also read: AGENTS.md");
        return new ContextReport(Id, "characters", null, null, files, []);
    }
}

/// <summary>
/// OpenCode (opencode.ai/docs/rules): ~/.config/opencode/AGENTS.md, else ~/.claude/CLAUDE.md; the first AGENTS.md (else CLAUDE.md) found going up
/// from the start folder; files named by "instructions" in opencode.json (paths and globs; web addresses are reported, not read).
/// OPENCODE_DISABLE_CLAUDE_CODE(_PROMPT) turns the Claude Code files off. No size limit is documented.
/// </summary>
internal sealed class OpenCodeContext : IContextRule
{
    private static readonly JsonDocumentOptions _jsonc = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    public string Id => "opencode";

    public ContextReport Read(string project, string start, string home)
    {
        var claude = Environment.GetEnvironmentVariable("OPENCODE_DISABLE_CLAUDE_CODE") is not ("1" or "true");
        var claudePrompt = claude && Environment.GetEnvironmentVariable("OPENCODE_DISABLE_CLAUDE_CODE_PROMPT") is not ("1" or "true");
        var config = Path.Combine(home, ".config", "opencode");
        var files = new List<ContextFile>(); var findings = new List<Finding>();

        var global = Path.Combine(config, "AGENTS.md");
        if (File.Exists(global))
        {
            files.Add(Measure(global, LoadWhen.Always, null, "Global"));
        }
        else if (claudePrompt && File.Exists(Path.Combine(home, ".claude", "CLAUDE.md")))
        {
            files.Add(Measure(Path.Combine(home, ".claude", "CLAUDE.md"), LoadWhen.Always, null, "Global · Claude Code's file (no opencode AGENTS.md)"));
        }

        foreach (var dir in Ancestors(start).AsEnumerable().Reverse())
        {
            var agents = Path.Combine(dir, "AGENTS.md");
            var claudeMd = Path.Combine(dir, "CLAUDE.md");
            if (File.Exists(agents)) { files.Add(Measure(agents, LoadWhen.Always, null, "First AGENTS.md going up")); break; }
            if (claude && File.Exists(claudeMd)) { files.Add(Measure(claudeMd, LoadWhen.Always, null, "Claude Code's file (no AGENTS.md)")); break; }
        }

        foreach (var (file, root) in new[] { (Path.Combine(config, "opencode.json"), config), (Path.Combine(config, "opencode.jsonc"), config),
            (Path.Combine(project, "opencode.json"), project), (Path.Combine(project, "opencode.jsonc"), project) })
        {
            Instructions(file, root, files, findings);
        }

        return new ContextReport(Id, "characters", null, null, files, findings);
    }

    /// <summary>"instructions": paths and globs relative to the config's folder, added at launch; web addresses noted.</summary>
    private static void Instructions(string config, string root, List<ContextFile> files, List<Finding> findings)
    {
        if (!File.Exists(config))
        {
            return;
        }

        List<string> list;
        try
        {
            using var doc = JsonDocument.Parse(ReadText(config), _jsonc);
            list = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("instructions", out var i) && i.ValueKind == JsonValueKind.Array
                ? [.. i.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)] : [];
        }
        catch (JsonException) { return; }

        var name = Path.GetFileName(config);
        List<string>? all = null;
        foreach (var entry in list)
        {
            if (SkillDoc.IsExternal(entry)) { findings.Add(new($"{name} also loads {entry} from the web at launch (not measured here).", false)); continue; }
            var rx = Glob.ToRegex(entry);
            var matched = entry.IndexOfAny(['*', '?', '{', '[']) < 0
                ? [entry]
                : (all ??= ProjectFiles.List(root, out _)).Where(r => rx.IsMatch(r)).ToList();
            foreach (var rel in matched)
            {
                var f = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(f) && !files.Any(x => Same(x.Path, f)))
                {
                    files.Add(Measure(f, LoadWhen.Always, null, $"instructions in {name}"));
                }
            }

            if (matched.Count == 0 || (matched.Count == 1 && !File.Exists(Path.Combine(root, matched[0]))))
            {
                findings.Add(new($"{name} lists \"{entry}\" in instructions, but no file matches it."));
            }
        }
    }
}
