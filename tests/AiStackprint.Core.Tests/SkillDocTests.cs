// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class SkillDocTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aistack-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private string Write(string rel, string text)
    {
        var p = Path.Combine(_tmp, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void Read_gives_the_whole_small_file_and_stops_a_large_one_at_the_limit()
    {
        var small = Write("s/SKILL.md", "---\nname: s\ndescription: ünïcode ✓\n---\nbody\n");
        Assert.Equal("ünïcode ✓", SkillDoc.Read(small).Get("description"));
        Assert.Equal("body\n", SkillDoc.Read(small).Body);

        var big = Write("b/SKILL.md", "---\nname: b\n---\n" + new string('x', SkillDoc.MaxBytes + 1000));
        Assert.Equal(SkillDoc.MaxBytes, SkillDoc.Read(big).Body.Length + "---\nname: b\n---\n".Length);
    }

    [Fact]
    public void Entries_list_size_and_time_from_the_folder_listing()
    {
        var f = Write("e/SKILL.md", "abc");
        Write("e/.git/x", "hidden");
        var e = Assert.Single(SkillDoc.Entries(Path.Combine(_tmp, "e")));
        Assert.Equal(("SKILL.md", 3L, File.GetLastWriteTimeUtc(f).Ticks), e);
        Assert.Equal(e, SkillDoc.Entry(f));
    }

    [Fact]
    public void Parses_front_matter_blocks_nested_maps_and_lists()
    {
        var d = SkillDoc.Parse("---\nname: pdf-tools\ndescription: >\n  Reads PDFs.\n  Use for forms.\nlicense: \"MIT\"\nmetadata:\n  author: acme\n  version: \"1.0\"\nallowed-tools:\n  - Read\n  - Bash(git:*)\n---\n# Body\ntext\n");
        Assert.True(d.HasFront && d.FrontClosed);
        Assert.Equal("pdf-tools", d.Get("name"));
        Assert.Equal("Reads PDFs. Use for forms.", d.Get("description"));
        Assert.Equal("MIT", d.Get("license"));
        Assert.Equal("acme", d.Get("metadata.author"));
        Assert.Equal("1.0", d.Get("metadata.version"));
        Assert.Equal("Read, Bash(git:*)", d.Get("allowed-tools"));
        Assert.Equal("# Body\ntext\n", d.Body);
        Assert.Equal(15, d.Lines);
        Assert.False(SkillDoc.Parse("no front matter").HasFront);
        Assert.False(SkillDoc.Parse("---\nname: x\nbody").FrontClosed);
    }

    [Fact]
    public void Resolve_keeps_links_inside_the_folder()
    {
        var root = Path.Combine(_tmp, "skill");
        var md = Write("skill/SKILL.md", "x");
        Assert.Equal(Path.Combine(root, "references", "api.md"), SkillDoc.Resolve(root, md, "references/api.md#usage"));
        Assert.Equal(Path.Combine(root, "my file.md"), SkillDoc.Resolve(root, md, "my%20file.md"));
        Assert.Null(SkillDoc.Resolve(root, md, "../other/secret.md"));
        Assert.Null(SkillDoc.Resolve(root, md, "https://example.com/a.md"));
        Assert.Null(SkillDoc.Resolve(root, md, "#section"));
        Assert.Equal(new[] { "a.md", "img.png" }, SkillDoc.Links("[A](a.md) ![i](img.png \"t\") [A again](a.md)"));
    }

    [Fact]
    public void Check_reports_format_rules_and_links()
    {
        var md = Write("Bad_Skill/SKILL.md", "---\nname: Bad_Skill\n---\n[ok](ref/a.md) [gone](ref/missing.md) [out](../x.md) [web](https://x.y)\n" + string.Join("\n", Enumerable.Repeat("l", 520)));
        Write("Bad_Skill/ref/a.md", "see [b](b.md)");
        Write("Bad_Skill/ref/b.md", "b");
        var f = SkillCheck.Check(md, SkillDoc.Read(md)).Select(x => x.Text).ToList();
        Assert.Contains(f, t => t.StartsWith("Name should use only lowercase"));
        Assert.Contains(f, t => t.StartsWith("No description"));
        Assert.Contains(f, t => t == "Broken link: ref/missing.md");
        Assert.Contains(f, t => t == "Link leaves the skill folder: ../x.md");
        Assert.Contains(f, t => t.StartsWith("ref/a.md links on to b.md"));
        Assert.Contains(f, t => t.Contains("lines; keep it under 500"));
        Assert.DoesNotContain(f, t => t.Contains("x.y"));

        var good = Write("good-skill/SKILL.md", "---\nname: good-skill\ndescription: Does a thing. Use when asked.\n---\nbody\n");
        Assert.Empty(SkillCheck.Check(good, SkillDoc.Read(good)));
        var other = Write("folder/SKILL.md", "---\nname: other-name\ndescription: d\n---\n");
        Assert.Single(SkillCheck.Check(other, SkillDoc.Read(other)), x => !x.Warning && x.Text.Contains("differs from its folder"));
    }

    [Fact]
    public void Plugin_manifest_gives_description_author_and_keywords_and_skills_give_author()
    {
        Write("plug/.claude-plugin/plugin.json", "{ \"description\": \"Formats code\", \"author\": { \"name\": \"Acme\" }, \"keywords\": [\"format\", \"lint\", \"Format\", 3, \" \"], }");
        Write("plug2/.claude-plugin/plugin.json", "{ \"author\": \"Solo\" }");
        string Esc(string p) => Path.Combine(_tmp, p).Replace("\\", "\\\\");
        var registry = Write("installed_plugins.json", $$"""
            { "plugins": { "fmt@acme-market": [ { "scope": "user", "version": "2.5.1", "installPath": "{{Esc("plug")}}" } ],
                           "solo@market": [ { "version": "1.0.0", "installPath": "{{Esc("plug2")}}" } ],
                           "bare@shop": [ { "installPath": "{{Esc("none")}}" } ] } }
            """);
        var found = new List<Item>();
        PluginReaders.Find("claude")!.Read(registry, new AppDefinition
        {
            Id = "claude-code",
            Name = "Claude Code"
        }, found);
        var by = found.Where(i => i.Kind == "Plugin").ToDictionary(i => i.Name);
        Assert.Equal(("Formats code", "Acme", "2.5.1"), (by["fmt@acme-market"].Description, by["fmt@acme-market"].Author, by["fmt@acme-market"].Version));
        Assert.Equal(["format", "lint"], by["fmt@acme-market"].Keywords);   // strings only, no blanks or repeats
        Assert.Equal(("", "Solo"), (by["solo@market"].Description, by["solo@market"].Author));
        Assert.Empty(by["solo@market"].Keywords);
        Assert.Equal("shop", by["bare@shop"].Author);   // no manifest author: the marketplace
        Assert.Null(by["bare@shop"].Version);

        Write("skills/one/SKILL.md", "---\nname: one\ndescription: d\nmetadata:\n  author: meta-author\n---\n");
        Write("skills/two/SKILL.md", "---\nname: two\nauthor: top\n\n---\n");
        var rows = new List<Item>();
        Scanner.ReadRoot(Path.Combine(_tmp, "skills"), new ScanRoot(), "App", rows);
        Assert.Equal("meta-author", rows.Single(r => r.Name == "one").Author);
        Assert.Equal("top", rows.Single(r => r.Name == "two").Author);
    }

    [Fact]
    public void Front_matter_block_scalars_keep_their_blank_lines_and_lists_and_nested_keys_are_read()
    {
        var d = SkillDoc.Parse("---\ndescription: |\n  first line\n\n  # inside the block\n  second\ntags:\n- a\n- \"b\"\nnot a key line\nmetadata:\n  owner: me\n---\nbody\n");
        Assert.StartsWith("first line\n\n", d.Get("description"), StringComparison.Ordinal);
        Assert.EndsWith("second", d.Get("description"), StringComparison.Ordinal);
        Assert.Equal("a, b", d.Get("tags"));
        Assert.Equal("me", d.Get("metadata.owner"));
        Assert.Equal("", d.Get("missing"));
    }

    [Fact]
    public void Links_that_are_only_a_query_or_not_a_path_do_not_resolve()
    {
        var root = Path.Combine(_tmp, "skill");
        var from = Path.Combine(root, "SKILL.md");
        Assert.Null(SkillDoc.Resolve(root, from, "?x=1"));
        Assert.Null(SkillDoc.Resolve(root, from, "a\0b.md"));
        Assert.Null(SkillDoc.Resolve(root, from, "../outside.md"));
        Assert.Equal(Path.Combine(root, "ref", "a b.md"), SkillDoc.Resolve(root, from, "ref/a%20b.md#part"));
    }

    [Fact]
    public void Files_skip_hidden_folders_but_not_the_plugin_folder_and_stop_at_the_limit()
    {
        var root = Path.Combine(_tmp, "plugin");
        foreach (var rel in new[] { "SKILL.md", ".git/config", ".claude-plugin/plugin.json", "deep/a.md" })
        {
            var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "x");
        }

        var all = SkillDoc.Files(root).Select(f => f.Rel.Replace('\\', '/')).ToList();
        Assert.Equal("SKILL.md", all[0]);   // the top folder first
        Assert.Contains(".claude-plugin/plugin.json", all);
        Assert.DoesNotContain(".git/config", all);
        Assert.Single(SkillDoc.Files(root, 1));
        Assert.Empty(SkillDoc.Files(Path.Combine(_tmp, "none")));
        Assert.Equal(("SKILL.md", 1L), (SkillDoc.Entry(Path.Combine(root, "SKILL.md")).Rel, SkillDoc.Entry(Path.Combine(root, "SKILL.md")).Size));
    }
}
