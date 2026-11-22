// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>The files each app's rule finds: global files, the fall-backs to Claude Code's files, triggers and the findings about them.</summary>
public sealed class ContextRuleFilesTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("sp-rules-").FullName;
    private readonly string _home, _project;

    public ContextRuleFilesTests()
    {
        _home = Directory.CreateDirectory(Path.Combine(_tmp, "home")).FullName;
        _project = Directory.CreateDirectory(Path.Combine(_tmp, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(_project, ".git"));
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    private static void Write(string dir, string rel, string text)
    {
        var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    private ContextReport Read(string id, string? start = null) => ContextBudget.Find(id)!.Read(_project, start ?? _project, _home);

    private static string? Note(ContextReport r, string name) => r.Files.FirstOrDefault(f => Path.GetFileName(f.Path) == name)?.Note;

    [Fact]
    public void Windsurf_reads_its_global_rules_and_each_trigger()
    {
        Write(_home, ".codeium/windsurf/memories/global_rules.md", "be kind");
        Write(_project, ".windsurf/rules/decide.md", "---\ntrigger: model_decision\n---\nx");
        Write(_project, ".windsurf/rules/manual.md", "---\ntrigger: manual\n---\nx");
        Write(_project, ".devin/rules/none.md", "x");
        var r = Read("windsurf");
        Assert.Equal("Global rules", Note(r, "global_rules.md"));
        Assert.Equal(LoadWhen.AgentDecides, r.Files.Single(f => f.Path.EndsWith("decide.md", StringComparison.Ordinal)).Load);
        Assert.Equal("Only when you @mention it", Note(r, "manual.md"));
        Assert.Equal("No trigger in its front matter", Note(r, "none.md"));
    }

    [Fact]
    public void OpenCode_falls_back_to_claude_code_files_and_checks_its_instructions()
    {
        Write(_home, ".claude/CLAUDE.md", "global");
        Write(_project, "CLAUDE.md", "project");
        Write(_project, "docs/style.md", "style");
        Write(_project, "opencode.json", """{ "instructions": ["docs/*.md", "missing.md", "https://example.com/rules.md"] }""");
        Write(_home, ".config/opencode/opencode.json", "{ not json");
        var r = Read("opencode");
        Assert.Contains(r.Files, f => f.Note == "Global · Claude Code's file (no opencode AGENTS.md)");
        Assert.Contains(r.Files, f => f.Note == "Claude Code's file (no AGENTS.md)");
        Assert.Equal("instructions in opencode.json", Note(r, "style.md"));
        Assert.Contains(r.Findings, f => f.Text.Contains("\"missing.md\" in instructions, but no file matches it", StringComparison.Ordinal));
        Assert.Contains(r.Findings, f => f.Text.Contains("from the web at launch", StringComparison.Ordinal));

        Write(_home, ".config/opencode/AGENTS.md", "own");
        Write(_project, "AGENTS.md", "own");
        r = Read("opencode");
        Assert.Equal(["Global", "First AGENTS.md going up"], r.Files.Take(2).Select(f => f.Note));
    }

    [Fact]
    public void Gemini_files_in_sub_folders_load_when_a_tool_reads_there()
    {
        Write(_project, "GEMINI.md", "root");
        Write(_project, "pkg/GEMINI.md", "pkg");
        var r = Read("gemini");
        Assert.Equal("When a tool reads that folder", r.Files.Single(f => f.Path.Contains("pkg", StringComparison.Ordinal)).Note);
    }

    [Fact]
    public void Copilot_reads_the_repository_file_and_cursor_warns_about_long_and_old_rule_files()
    {
        Write(_project, ".github/copilot-instructions.md", "repo");
        Assert.Equal("Repository-wide", Note(Read("copilot"), "copilot-instructions.md"));

        Write(_project, ".cursor/rules/long.mdc", "---\nalwaysApply: true\n---\n" + string.Concat(Enumerable.Repeat("line\n", 600)));
        Write(_project, ".cursorrules", "old");
        Write(_project, "AGENTS.md", "agents");
        var r = Read("cursor");
        Assert.Contains(r.Findings, f => f.Text.StartsWith("long.mdc has", StringComparison.Ordinal));
        Assert.Contains(r.Findings, f => f.Text.StartsWith(".cursorrules is deprecated", StringComparison.Ordinal));
        Assert.Equal(LoadWhen.Always, r.Files.Single(f => Path.GetFileName(f.Path) == "AGENTS.md").Load);
    }
}
