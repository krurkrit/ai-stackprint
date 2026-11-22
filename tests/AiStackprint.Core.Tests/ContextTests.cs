// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

[Collection("Environment")]   // sets CODEX_HOME / CLAUDE_CONFIG_DIR
public sealed class ContextTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aistack-").FullName;
    private readonly string _home, _project;

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

    private static void Write(string dir, string rel, string text)
    {
        var p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    private static readonly AiStackprint.Models.AppDefinition _codex = new()
    {
        Id = "codex",
        Name = "Codex",
        Context = "codex",
        Configs = { new AiStackprint.Models.ConfigFile
        {
            Path = "AGENTS.md",
            Level = "Project",
            Kind = "instructions"
        } },
    };

    [Fact]
    public void A_codex_chain_over_the_limit_in_a_sub_folder_is_reported_once_with_its_start_folder()
    {
        Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(_home, ".codex"));
        Write(_project, "AGENTS.md", new string('a', 40_000));   // cut at the root already: reported there, not again for each folder
        Write(_project, "svc/a/AGENTS.md", "a rules");
        Write(_project, "svc/b/AGENTS.md", "b rules");
        Write(_project, "node_modules/x/AGENTS.md", "skipped");

        var issues = ProjectChecks.Run(_project, _project, [_codex], _home).Issues.Where(i => i.Finding.Text.StartsWith("When ", StringComparison.Ordinal)).ToList();

        Assert.Equal(["When Codex starts in svc/a: ", "When Codex starts in svc/b: "], issues.Select(i => i.Finding.Text[..(i.Finding.Text.IndexOf(": ", StringComparison.Ordinal) + 2)]));
        Assert.All(issues, i => Assert.Contains("is not loaded", i.Finding.Text, StringComparison.Ordinal));
        Assert.Equal(Path.Combine(_project, "svc", "a"), issues[0].File);
        Assert.DoesNotContain(ProjectChecks.Run(_project, Path.Combine(_project, "svc", "a"), [_codex], _home).Issues, i => i.Finding.Text.StartsWith("When ", StringComparison.Ordinal));   // a chosen start folder: only that one
    }

    [Fact]
    public void At_most_fifty_start_folders_are_checked()
    {
        for (var i = 0; i < 60; i++)
        {
            Write(_project, $"pkg/p{i:00}/AGENTS.md", "x");
        }

        var folders = ProjectChecks.StartFolders(_project, [_codex]);
        Assert.Equal(ProjectChecks.MaxStartFolders, folders.Count);
        Assert.Equal(Path.Combine(_project, "pkg", "p00"), folders[0]);
    }

    [Fact]
    public async Task Within_one_run_a_file_is_read_once_and_after_it_again()
    {
        Write(_project, "AGENTS.md", "first");
        var path = Path.Combine(_project, "AGENTS.md");
        using (ContextBudget.ReadOnce())
        {
            Assert.Equal("first", ContextBudget.ReadText(path));
            File.WriteAllText(path, "second");
            Assert.Equal("first", await Task.Run(() => ContextBudget.ReadText(path), TestContext.Current.CancellationToken));   // the parallel work of the run shares it
        }

        Assert.Equal("second", ContextBudget.ReadText(path));
    }

    [Fact]
    public void Below_finds_the_wanted_names_three_folders_down_in_name_order_skipping_hidden_and_build_folders()
    {
        foreach (var rel in new[] { "b/CLAUDE.md", "a/x/y/CLAUDE.md", "a/x/y/z/CLAUDE.md", "a/AGENTS.md", "a/CLAUDE.md", ".hidden/CLAUDE.md", "node_modules/CLAUDE.md", "a/other.md" })
        {
            Write(_project, rel, "x");
        }

        var found = ContextBudget.Below(_project, ["CLAUDE.md", "AGENTS.md"]).Select(f => Path.GetRelativePath(_project, f).Replace('\\', '/'));
        Assert.Equal(["a/CLAUDE.md", "a/AGENTS.md", "a/x/y/CLAUDE.md", "b/CLAUDE.md"], found);   // the names in the order asked, folder by folder; z is too deep
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
        Assert.Equal(["AGENTS.md", "AGENTS.md", "AGENTS.override.md", "TEAM.md"], r.Files.Select(f => Path.GetFileName(f.Path)));
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
