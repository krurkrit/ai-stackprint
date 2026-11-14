// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class AgentsExportTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiexport-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private string Write(string rel, string text)
    {
        var p = Path.Combine(_tmp, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    private static ContextReport Report(params ContextFile[] files) => new("app", "characters", null, null, [.. files], []);
    private static ContextFile F(string path, LoadWhen load = LoadWhen.Always, IReadOnlyList<string>? patterns = null) => new(path, load, 10, 10, Patterns: patterns);

    [Fact]
    public void Combines_loaded_files_drops_repeated_lines_and_skips_personal_and_outside_files()
    {
        var claude = Write("CLAUDE.md", "# Rules\n\nUse tabs for indentation everywhere.\nRun the tests before finishing.\n@AGENTS.md\n");
        var gemini = Write("GEMINI.md", "Use tabs for indentation everywhere.\nPrefer small commits with clear messages.\n");
        var rule = Write(".claude/rules/ts.md", "---\npaths:\n  - \"src/**/*.ts\"\n---\nUse strict mode in TypeScript.\n");
        var local = Write("CLAUDE.local.md", "my private note that must not leak");
        var agents = Write("AGENTS.md", "existing");
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere.md");
        var manual = Write("docs/manual.md", "Only when mentioned in a prompt.");
        var (sources, text) = AgentsExport.Build(_tmp, [Report(F(claude), F(local), F(agents), F(outside), F(manual, LoadWhen.Manual)), Report(F(gemini), F(rule, LoadWhen.Conditional, ["src/**/*.ts"]))], new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Local));
        Assert.Equal(["CLAUDE.md", "GEMINI.md", ".claude/rules/ts.md"], sources);
        Assert.NotNull(text);
        Assert.StartsWith("# AGENTS.md", text);
        Assert.Contains("Exported by AI Stackprint on 2026-11-14 from CLAUDE.md, GEMINI.md, .claude/rules/ts.md", text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "Use tabs for indentation everywhere"));
        Assert.Contains("Prefer small commits", text);
        Assert.Contains("Applies to: src/**/*.ts", text);
        Assert.Contains("Use strict mode in TypeScript.", text);
        Assert.DoesNotContain("paths:", text);
        Assert.DoesNotContain("@AGENTS.md", text.Replace("AGENTS.md\n\n<!--", ""));
        Assert.DoesNotContain("private note", text);
        Assert.DoesNotContain("Only when mentioned", text);
    }

    [Fact]
    public void Nothing_to_export_gives_null()
    {
        Assert.Null(AgentsExport.Build(_tmp, [Report()], DateTime.Today).Text);
    }

    [Fact]
    public void Create_refuses_an_existing_file_and_replace_backs_up_and_refuses_after_an_outside_change()
    {
        var file = Path.Combine(_tmp, "out", "AGENTS.md");
        ConfigEditor.Create(file, "one");
        Assert.Equal("one", File.ReadAllText(file));
        Assert.Throws<IOException>(() => ConfigEditor.Create(file, "two"));

        var stamp = ConfigEditor.StampOf(file);
        var backups = Path.Combine(_tmp, "backups");
        var backup = ConfigEditor.Save(file, "three", stamp, backups);
        Assert.Equal("one", File.ReadAllText(backup));
        Assert.Equal("three", File.ReadAllText(file));

        File.WriteAllText(file, "changed outside, longer");
        Assert.Throws<FileChangedException>(() => ConfigEditor.Save(file, "four", stamp, backups));
        Assert.Equal("changed outside, longer", File.ReadAllText(file));
    }
}
