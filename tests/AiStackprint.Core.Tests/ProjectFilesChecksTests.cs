// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

[Collection("Environment")]
public sealed class ProjectFilesChecksTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aifiles-").FullName;
    private string Project => Path.Combine(_tmp, "repo");
    private string Home => Path.Combine(_tmp, "home");
    public void Dispose() => Directory.Delete(_tmp, true);

    private void Write(string rel, string text)
    {
        var p = Path.Combine(Project, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    [Fact]
    public void Ignore_patterns_count_hidden_files_and_dead_patterns()
    {
        string[] files = ["src/a.ts", "src/b.ts", "secrets/key.txt", "docs/readme.md", "app.log"];
        var r = GitIgnore.Apply("*.log\nsecrets/\n!secrets/key.txt\nmissing/\n# comment\n*.nothing\n", files);
        Assert.Equal(2, r.Hidden);   // app.log, and secrets/key.txt: an ignored folder cannot be re-included
        Assert.Equal(["missing/", "*.nothing"], r.Dead);
    }

    [Fact]
    public void Ignore_patterns_count_files_in_nested_ignored_folders_once_and_a_pattern_used_below_one_is_not_dead()
    {
        string[] files = ["build/out/a.dll", "build/out/b.dll", "build/x.txt", "src/build.cs", "src/deep/c.tmp", "Src/Deep/D.TMP"];
        var r = GitIgnore.Apply("build/\n*.dll\n*.tmp\n!src/deep/c.tmp\n", files);
        Assert.Equal(4, r.Hidden);   // the three in build/, and D.TMP (case is ignored); c.tmp is taken back
        Assert.Empty(r.Dead);   // *.dll matches only inside build/, which is ignored anyway
    }

    [Fact]
    public void Ignore_files_of_apps_are_listed_with_their_counts()
    {
        Write("src/a.ts", "x");
        Write("app.log", "x");
        Write(".cursorignore", "*.log\nnothing/\n");
        var defs = new[] { new AppDefinition
        {
            Id = "cursor",
            Name = "Cursor",
            Configs = { new ConfigFile
            {
                Path = ".cursorignore",
                Level = "Project",
                Kind = "ignore"
            } }
        } };
        var check = ProjectChecks.Run(Project, Project, defs, Home);
        var ig = Assert.Single(check.Ignores);
        Assert.Equal(1, ig.Hidden);
        Assert.Equal(3, ig.Total);   // a.ts, app.log, .cursorignore
        Assert.Contains(check.Issues, i => i.Finding.Text.Contains("match no file") && i.Finding.Text.Contains("nothing/") && !i.Finding.Warning);
    }

    [Fact]
    public void Stale_paths_in_instruction_files_are_reported_but_not_code_fences_urls_or_globs()
    {
        Write("src/real.ts", "x");
        Write("docs/guide.md", "g");
        Write("CLAUDE.md", "See `src/real.ts` and `docs/guide.md` and `src/gone.ts` and `docs/old/plan.md:12`.\n"
            + "Not paths: `https://x.io/a/b`, `src/**/*.ts`, `path/to/file.md`, `npm run build`, `bin/Debug/app.dll`.\n"
            + "```\nsrc/in-fence.ts\n```\n");
        var defs = new[] { new AppDefinition
        {
            Id = "claude-code",
            Name = "Claude Code",
            Context = "claude",
            Configs = { new ConfigFile
            {
                Path = "CLAUDE.md",
                Level = "Project"
            } }
        } };
        var issue = Assert.Single(ProjectChecks.Run(Project, Project, defs, Home).Issues, i => i.Finding.Text.Contains("paths that do not exist"));
        Assert.Contains("src/gone.ts", issue.Finding.Text);
        Assert.Contains("docs/old/plan.md:12", issue.Finding.Text);
        Assert.DoesNotContain("real.ts", issue.Finding.Text);
        Assert.DoesNotContain("in-fence", issue.Finding.Text);
        Assert.DoesNotContain("example.com", issue.Finding.Text);
        Assert.DoesNotContain("Debug", issue.Finding.Text);
        Assert.False(issue.Finding.Warning);
    }
}
