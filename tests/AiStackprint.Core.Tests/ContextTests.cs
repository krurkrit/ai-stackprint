// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

[Collection("Environment")]   // sets CODEX_HOME / CLAUDE_CONFIG_DIR
public class ContextTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("aistack-").FullName;
    readonly string _home, _project;

    public ContextTests()
    {
        _home = Directory.CreateDirectory(Path.Combine(_tmp, "home")).FullName;
        _project = Directory.CreateDirectory(Path.Combine(_tmp, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(_project, ".git"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODEX_HOME", null);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        Directory.Delete(_tmp, true);
    }

    string Write(string dir, string rel, string text)
    {
        var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void Imports_skip_code_quotes_and_mail()
    {
        var md = "See @docs/a.md and @~/notes.md.\n`@not/this.md` and \"@quoted.md\" mail me@example.com\n```\n@fenced.md\n```\n@My\\ Docs/x.md";
        Assert.Equal(new[] { "docs/a.md", "~/notes.md", "My Docs/x.md" }, ContextBudget.Imports(md));
    }

    [Fact]
    public void Claude_loads_chain_imports_local_rules_and_subfolders()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(_home, ".claude"));
        Write(_home, ".claude/CLAUDE.md", "user");
        Write(_project, "CLAUDE.md", "root @docs/a.md @missing.md");
        Write(_project, "docs/a.md", "a @b.md");
        Write(_project, "docs/b.md", "b");
        Write(_project, "CLAUDE.local.md", "mine");
        Write(_project, ".claude/rules/ts.md", "---\npaths: src/**/*.ts\n---\nts rules");
        Write(_project, ".claude/rules/all.md", "always");
        Write(_project, "pkg/CLAUDE.md", "pkg");
        var r = ContextBudget.Find("claude")!.Read(_project, _project, _home);
        LoadWhen Load(string rel) => r.Files.Single(f => f.Path == Path.GetFullPath(Path.Combine(_project, rel))).Load;
        Assert.Equal(LoadWhen.Always, Load("CLAUDE.md"));
        Assert.Equal(LoadWhen.Imported, Load("docs/a.md"));
        Assert.Equal(LoadWhen.Imported, Load("docs/b.md"));
        Assert.Equal(LoadWhen.Always, Load("CLAUDE.local.md"));
        Assert.Equal(LoadWhen.Conditional, Load(".claude/rules/ts.md"));
        Assert.Equal(LoadWhen.Always, Load(".claude/rules/all.md"));
        Assert.Equal(LoadWhen.OnDemand, Load("pkg/CLAUDE.md"));
        Assert.Contains(r.Files, f => f.Path == Path.Combine(_home, ".claude", "CLAUDE.md"));
        Assert.Contains(r.Findings, f => f.Text.Contains("@missing.md"));
        Assert.Equal("characters", r.Unit);

        Write(_project, "CLAUDE.md", new string('x', 40_001));
        var big = ContextBudget.Find("claude")!.Read(_project, _project, _home);
        Assert.Contains(big.Findings, f => f.Warning && f.Text.Contains("40,001"));
    }

    [Fact]
    public void Codex_takes_one_file_per_folder_and_cuts_at_the_byte_limit()
    {
        var codex = Path.Combine(_home, ".codex");
        Environment.SetEnvironmentVariable("CODEX_HOME", codex);
        Write(codex, "AGENTS.md", new string('g', 50_000));   // global: listed, not counted
        Write(codex, "config.toml", "project_doc_fallback_filenames = [\"TEAM.md\"]\n");
        Write(_project, "AGENTS.md", new string('a', 20_000));
        Write(_project, "svc/AGENTS.md", "skipped");
        Write(_project, "svc/AGENTS.override.md", new string('o', 20_000));
        Write(_project, "svc/api/TEAM.md", "team");
        var start = Path.Combine(_project, "svc", "api");
        var r = ContextBudget.Find("codex")!.Read(_project, start, _home);
        Assert.Equal("bytes", r.Unit);
        Assert.Equal(32 * 1024, r.Limit);
        Assert.Equal(new[] { "AGENTS.md", "AGENTS.md", "AGENTS.override.md", "TEAM.md" }, r.Files.Select(f => Path.GetFileName(f.Path)));
        Assert.False(r.Files[0].Counted);
        Assert.Equal(32 * 1024 - 20_000, r.Files[2].Kept);
        Assert.Equal(0, r.Files[3].Kept);
        Assert.Equal(32 * 1024, r.AlwaysTotal);
        Assert.Equal(2, r.Findings.Count(f => f.Warning));

        Write(codex, "config.toml", "project_doc_max_bytes = 65536\n");
        Assert.DoesNotContain(ContextBudget.Find("codex")!.Read(_project, start, _home).Files, f => f.Kept != null);
        Write(codex, "config.toml", "project_doc_max_bytes = 99999999999999999999999\n");   // not a number Codex could use: the default
        Assert.Equal(32 * 1024, ContextBudget.Find("codex")!.Read(_project, start, _home).Limit);
    }

    [Fact]
    public void Cursor_rules_by_front_matter_and_plain_md_is_ignored()
    {
        Write(_project, ".cursor/rules/always.mdc", "---\nalwaysApply: true\n---\nx");
        Write(_project, ".cursor/rules/ts.mdc", "---\nglobs: \"**/*.ts\"\nalwaysApply: false\n---\nx");
        Write(_project, ".cursor/rules/rpc.mdc", "---\ndescription: RPC patterns\n---\nx");
        Write(_project, ".cursor/rules/manual.mdc", "x");
        Write(_project, ".cursor/rules/notes.md", "x");
        var r = ContextBudget.Find("cursor")!.Read(_project, _project, _home);
        var by = r.Files.ToDictionary(f => Path.GetFileName(f.Path), f => f.Load);
        Assert.Equal(LoadWhen.Always, by["always.mdc"]);
        Assert.Equal(LoadWhen.Conditional, by["ts.mdc"]);
        Assert.Equal(LoadWhen.AgentDecides, by["rpc.mdc"]);
        Assert.Equal(LoadWhen.Manual, by["manual.mdc"]);
        Assert.Equal(LoadWhen.Ignored, by["notes.md"]);
        Assert.Contains(r.Findings, f => f.Warning && f.Text.Contains("notes.md"));
    }
}

[CollectionDefinition("Environment", DisableParallelization = true)]
public class EnvironmentCollection { }
