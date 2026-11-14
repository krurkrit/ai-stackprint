// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

[Collection("Environment")]   // ContextTests sets CLAUDE_CONFIG_DIR
public sealed class ProjectChecksTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aistack-").FullName;
    private readonly string _home, _project;
    private static readonly List<AppDefinition> _defs = AppCatalog.BuiltIn();

    public ProjectChecksTests()
    {
        _home = Directory.CreateDirectory(Path.Combine(_tmp, "home")).FullName;
        _project = Directory.CreateDirectory(Path.Combine(_tmp, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(_project, ".git"));
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    private void Write(string rel, string text, string? root = null)
    {
        var p = Path.Combine(root ?? _project, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    private ProjectCheck Run() => ProjectChecks.Run(_project, _project, _defs, _home);

    [Theory]
    [InlineData("src/**/*.ts", "src/a/b.ts", true)]
    [InlineData("src/**/*.ts", "src/b.ts", true)]
    [InlineData("*.ts", "deep/x/y.ts", true)]
    [InlineData("**/*.{ts,tsx}", "app/View.TSX", true)]
    [InlineData("docs", "docs/guide.md", true)]
    [InlineData("src/*.cs", "src/a/b.cs", false)]
    [InlineData("test?.py", "test10.py", false)]
    [InlineData("[!a]*.md", "b.md", true)]
    [InlineData("src/{a", "src/{a", true)]   // broken pattern: counted as matching
    public void Glob_matches_like_rule_files_expect(string pattern, string path, bool match) =>
        Assert.Equal(match, Glob.ToRegex(pattern).IsMatch(path));

    [Fact]
    public void Glob_split_keeps_braces_and_reads_yaml_lists()
    {
        Assert.Equal(["src/**/*.{ts,tsx}", "*.md"], Glob.Split("src/**/*.{ts,tsx}, \"*.md\""));
        Assert.Equal(["a/**", "b"], Glob.Split("[\"a/**\", 'b']"));
    }

    [Fact]
    public void Rules_whose_patterns_match_nothing_are_reported()
    {
        Write("src/app.ts", "x");
        Write(".claude/rules/ts.md", "---\npaths:\n  - \"src/**/*.ts\"\n---\nUse strict mode.");
        Write(".claude/rules/py.md", "---\npaths: \"**/*.py\"\n---\nUse black.");
        Write(".github/instructions/go.instructions.md", "---\napplyTo: \"**/*.go\"\n---\nGo rules.");
        Write(".kiro/steering/api.md", "---\ninclusion: fileMatch\nfileMatchPattern: \"api/**\"\n---\nAPI rules.");
        var issues = Run().Issues.Where(i => i.Finding.Text.Contains("never loads")).Select(i => Path.GetFileName(i.File)).Order().ToList();
        Assert.Equal(["api.md", "go.instructions.md", "py.md"], issues);
    }

    [Fact]
    public void Text_loaded_twice_at_launch_empty_files_and_broken_links()
    {
        var shared = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"- Rule number {i}: keep the build green."));
        Write("CLAUDE.md", shared + "\nSee [guide](docs/guide.md) and [api](docs/api.md#x) and [site](https://x.org).\n```\n[code](nope.md)\n```");
        Write(".claude/CLAUDE.md", shared);
        Write("docs/guide.md", "g");
        Write("AGENTS.md", "   \n");
        var issues = Run().Issues;

        var twice = Assert.Single(issues, i => i.Finding.Text.Contains("share"));
        Assert.Contains("claude-code", twice.Apps);
        Assert.Contains("share 10 lines", twice.Finding.Text);
        Assert.Contains(issues, i => i.Finding.Text == "AGENTS.md is empty.");
        var links = Assert.Single(issues, i => i.Finding.Text.Contains("links to"));
        Assert.Contains("docs/api.md#x", links.Finding.Text);
        Assert.DoesNotContain("nope.md", links.Finding.Text);
    }

    [Fact]
    public void Config_files_report_secrets_without_values_and_list_programs()
    {
        Write(".mcp.json", """
            {
              "mcpServers": {
                "github": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-github"], "env": { "GITHUB_TOKEN": "ghp_abcdefghijklmnopqrstuvwxyz0123456789" } },
                "pinned": { "command": "uvx", "args": ["mcp-server-git==1.2.0"] },
                "remote": { "url": "https://mcp.example.com/sse?api_key=supersecretvalue123" },
                "win": { "command": "cmd", "args": ["/c", "npx", "-y", "tool@latest", "--api-key", "abc123abc123abc123"] }
              }
            }
            """);
        Write(".claude/settings.json", """
            { "permissions": { "defaultMode": "bypassPermissions", "allow": ["Bash(*)", "Read"] },
              "env": { "OPENAI_API_KEY": "${OPENAI_API_KEY}", "API_TOKEN": "sk-proj-abcdefghijklmnopqrstuvwxyz" },
              "hooks": { "PostToolUse": [ { "matcher": "Edit", "hooks": [ { "type": "command", "command": "dotnet format --token=hunter2hunter2" } ] } ] } }
            """);
        var r = Run();
        var text = string.Join("\n", r.Issues.Select(i => i.Finding.Text)) + string.Join("\n", r.Programs.Select(p => p.What + p.Note));
        Assert.DoesNotContain("ghp_abc", text);
        Assert.DoesNotContain("sk-proj", text);
        Assert.DoesNotContain("supersecret", text);
        Assert.DoesNotContain("abc123", text);
        Assert.DoesNotContain("hunter2", text);

        Assert.Contains(r.Issues, i => i.Finding.Warning && i.Finding.Text.Contains("\"mcpServers.github.env.GITHUB_TOKEN\""));
        Assert.Contains(r.Issues, i => i.Finding.Text.Contains("\"env.API_TOKEN\""));
        Assert.DoesNotContain(r.Issues, i => i.Finding.Text.Contains("OPENAI_API_KEY"));   // a reference, not a value
        Assert.Contains(r.Issues, i => i.Finding.Text.Contains("bypassPermissions"));
        Assert.Contains(r.Issues, i => i.Finding.Text.Contains("\"Bash(*)\""));

        var servers = r.Programs.Where(p => p.Kind == "MCP server").ToDictionary(p => p.Name);
        Assert.Equal("Not pinned: @modelcontextprotocol/server-github · Key in plain text · env: GITHUB_TOKEN", servers["github"].Note);
        Assert.Null(servers["pinned"].Note);
        Assert.Equal("Not pinned: tool@latest · Key in plain text", servers["win"].Note);
        Assert.EndsWith("--api-key " + ConfigJson.Hidden, servers["win"].What);
        var hook = Assert.Single(r.Programs, p => p.Kind == "Hook");
        Assert.Equal("PostToolUse · Edit", hook.Name);
    }

    [Theory]
    [InlineData("apiKey", "sk-ant-api03-abcdefghijklmnopqrstuvwxyz", true)]
    [InlineData("token", "0123456789abcdef0123", true)]
    [InlineData("token", "${GITHUB_TOKEN}", false)]
    [InlineData("apiKey", "{env:OPENAI_API_KEY}", false)]
    [InlineData("apiKey", "your-api-key-goes-here", false)]
    [InlineData("description", "a long description of the server", false)]
    public void Secret_values_are_told_from_references(string name, string value, bool secret) =>
        Assert.Equal(secret, ConfigJson.LooksSecret(name, value));

    [Fact]
    public void New_apps_read_their_rule_files()
    {
        Write(".windsurf/rules/tests.md", "---\ntrigger: glob\nglobs: \"**/*.test.ts\"\n---\nx");
        Write(".windsurf/rules/always.md", "---\ntrigger: always_on\n---\nx");
        Write("AGENTS.md", "agents");
        Write("QWEN.md", "qwen");
        Write(".qwen/settings.json", "{}", _home);
        Write("opencode.json", """{ "instructions": ["docs/*.md", "https://x.org/rules.md"] }""");
        Write("docs/style.md", "style");
        var ctx = Run().Context.ToDictionary(c => c.App.Id, c => c.Report);

        var windsurf = ctx["windsurf"].Files.ToDictionary(f => Path.GetFileName(f.Path));
        Assert.Equal((LoadWhen.Conditional, LoadWhen.Always, LoadWhen.Always), (windsurf["tests.md"].Load, windsurf["always.md"].Load, windsurf["AGENTS.md"].Load));
        Assert.Equal(["**/*.test.ts"], windsurf["tests.md"].Patterns);
        Assert.Equal(["QWEN.md", "AGENTS.md"], ctx["qwen-code"].Files.Select(f => Path.GetFileName(f.Path)));
        Assert.Equal(["AGENTS.md", "style.md"], ctx["opencode"].Files.Select(f => Path.GetFileName(f.Path)));
        Assert.Contains(ctx["opencode"].Findings, f => f.Text.Contains("https://x.org/rules.md"));
        Assert.Equal(["AGENTS.md"], ctx["kiro"].Files.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void Personal_files_git_does_not_ignore_are_reported()
    {
        Write("CLAUDE.local.md", "mine");
        Write(".claude/settings.local.json", "{}");
        Write("AGENTS.local.md", "mine");   // Windsurf reads it
        Write(".gitignore", "AGENTS.local.md\n");
        Write(".gitconfig", "[core]\n\texcludesFile = ~/my-ignore\n", _home);   // the global excludes file
        Write("my-ignore", "*.local.json\n", _home);
        var personal = Run().Issues.Where(i => i.Finding.Text.Contains("personal file")).ToList();
        var one = Assert.Single(personal);
        Assert.Equal("CLAUDE.local.md", Path.GetFileName(one.File));
        Assert.Contains("claude-code", one.Apps);

        Directory.Delete(Path.Combine(_project, ".git"));   // not a git repository: nothing to commit
        Assert.DoesNotContain(Run().Issues, i => i.Finding.Text.Contains("personal file"));
    }
}
