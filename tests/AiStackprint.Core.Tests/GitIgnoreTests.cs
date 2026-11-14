// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class GitIgnoreTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aistack-").FullName;
    private readonly string _repo, _home;

    public GitIgnoreTests()
    {
        _repo = Directory.CreateDirectory(Path.Combine(_tmp, "repo")).FullName;
        _home = Directory.CreateDirectory(Path.Combine(_tmp, "home")).FullName;
        Directory.CreateDirectory(Path.Combine(_repo, ".git", "info"));
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    private void Write(string rel, string text)
    {
        var p = Path.Combine(_repo, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    [Theory]
    [InlineData("*.local.md", "CLAUDE.local.md", true)]
    [InlineData("*.local.md", "sub/CLAUDE.local.md", true)]
    [InlineData("/CLAUDE.local.md", "sub/CLAUDE.local.md", false)]
    [InlineData("/CLAUDE.local.md", "CLAUDE.local.md", true)]
    [InlineData(".claude/", ".claude/settings.local.json", true)]
    [InlineData("settings.local.json", ".claude/settings.local.json", true)]
    [InlineData("*.md\n!README.md", "README.md", false)]
    [InlineData(".claude/\n!.claude/keep.json", ".claude/keep.json", true)]   // a file in an ignored folder cannot be taken back
    [InlineData(".claude/*\n!.claude/keep.json", ".claude/keep.json", false)]
    [InlineData("docs/", "docs", false)]   // folders only; this is a file
    [InlineData("# a comment\n\n\\#hash", "#hash", true)]
    [InlineData("a/**/b.md", "a/x/y/b.md", true)]
    [InlineData("", "a.md", false)]
    public void Patterns_follow_gitignore_rules(string rules, string path, bool ignored)
    {
        Write(".gitignore", rules);
        Assert.Equal(ignored, new GitIgnore(_repo, _home).Ignored(path));
    }

    [Fact]
    public void Nearer_gitignore_wins_and_info_exclude_counts()
    {
        Write(".gitignore", "*.log\n");
        Write("sub/.gitignore", "!keep.log\n");
        Write(".git/info/exclude", "secret.txt\n");
        var git = new GitIgnore(_repo, _home);
        Assert.False(git.Ignored("sub/keep.log"));
        Assert.True(git.Ignored("sub/x.log"));
        Assert.True(git.Ignored("keep.log"));
        Assert.True(git.Ignored("deep/secret.txt"));
        Assert.Equal(_repo, GitIgnore.RepoOf(Directory.CreateDirectory(Path.Combine(_repo, "sub", "inner")).FullName));
    }
}
