// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class MarkdownHtmlTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiprof-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    [Fact]
    public void Readme_html_becomes_markdown_blocks()
    {
        var md = MarkdownLite.FromHtml("""
            <!-- hidden -->
            <h1 align="center"><img src="logo.png" alt="Logo"> Tool</h1>
            <p align="center"><a href="https://x.org/ci"><img alt="Build" src="b.svg"></a> <b>fast</b> &amp; <i>small</i></p>
            <details><summary>More</summary>
            <ul><li>one</li><li>two</li></ul>
            </details>
            Line<br>break
            """);
        var blocks = MarkdownLite.Blocks(md);
        Assert.DoesNotContain("<", md);
        Assert.DoesNotContain("hidden", md);
        Assert.Equal(new MdBlock(MdKind.Heading, "Logo Tool", 1), blocks[0]);
        Assert.Equal("[Build](https://x.org/ci) **fast** & *small*", blocks[1].Text);
        Assert.Equal("**More**", blocks[2].Text);
        Assert.Equal(["one", "two"], blocks.Where(b => b.Kind == MdKind.Bullet).Select(b => b.Text));
        Assert.Equal("Line\nbreak", blocks[^1].Text);
    }

    [Fact]
    public void Code_is_kept_as_written_and_html_code_becomes_markdown_code()
    {
        var md = MarkdownLite.FromHtml("Use `<div>` or <code>a &lt; b</code>.\n```html\n<p>kept</p>\n```\n<pre><code class=\"language-js\">if (a &lt; b) {}\n`x`</code></pre>");
        Assert.StartsWith("Use `<div>` or `a < b`.\n```html\n<p>kept</p>\n```", md);
        Assert.Contains("```js\nif (a < b) {}\n`x`\n```", md);
    }

    [Fact]
    public void Html_tables_become_markdown_tables_and_unknown_tags_stay()
    {
        var md = MarkdownLite.FromHtml("<table><tr><th>Key</th><th>Use</th></tr><tr><td><code>a</code></td><td>b|c</td></tr></table>\nList<string> stays");
        var table = MarkdownLite.Blocks(md).Single(b => b.Kind == MdKind.Table);
        Assert.Equal([["Key", "Use"], ["`a`", "b/c"]], table.Rows);
        Assert.EndsWith("List<string> stays", md);
        Assert.Equal("Plain text, no change.", MarkdownLite.FromHtml("Plain text, no change."));
    }

    [Fact]
    public void Plain_gives_one_line_without_marks()
    {
        Assert.Equal("Formats code with Prettier. See docs.",
            MarkdownLite.Plain("<p>Formats <b>code</b> with **Prettier**.</p>\n<br/>See [docs](https://x.org)."));
        Assert.Equal("Use run_tests for *.md files & x code", MarkdownLite.Plain("Use run_tests for *.md files &amp; `x` code"));   // globs and snake_case stay
        Assert.Equal("Title item one", MarkdownLite.Plain("# Title\n- item one"));
        Assert.Equal("just text", MarkdownLite.Plain("  just text "));
    }

    [Fact]
    public void Skill_descriptions_are_plain_and_read_whole_block_scalars()
    {
        var dir = Path.Combine(_tmp, "skills", "pdf");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\nname: pdf\ndescription: >\n  Reads <b>PDF</b> files.\n  Use for **forms**.\nlicense: MIT\n---\nbody\n");
        var rows = new List<Item>();
        Scanner.ReadRoot(Path.Combine(_tmp, "skills"), new SkillSource(), "App", rows);
        Assert.Equal("Reads PDF files. Use for forms.", rows.Single().Description);
    }
}
