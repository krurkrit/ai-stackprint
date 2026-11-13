using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class SkillDocTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("aistack-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    string Write(string rel, string text)
    {
        var p = Path.Combine(_tmp, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
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
    public void Plugin_manifest_gives_description_and_author_and_skills_give_author()
    {
        Write("plug/.claude-plugin/plugin.json", "{ \"description\": \"Formats code\", \"author\": { \"name\": \"Acme\" }, }");
        Write("plug2/.claude-plugin/plugin.json", "{ \"author\": \"Solo\" }");
        string Esc(string p) => Path.Combine(_tmp, p).Replace("\\", "\\\\");
        var registry = Write("installed_plugins.json", $$"""
            { "plugins": { "fmt@acme-market": [ { "scope": "user", "version": "2.5.1", "installPath": "{{Esc("plug")}}" } ],
                           "solo@market": [ { "version": "1.0.0", "installPath": "{{Esc("plug2")}}" } ],
                           "bare@shop": [ { "installPath": "{{Esc("none")}}" } ] } }
            """);
        var found = new List<Item>();
        PluginReaders.Find("claude")!.Read(registry, new AppDefinition { Id = "claude-code", Name = "Claude Code" }, found);
        var by = found.Where(i => i.Kind == "Plugin").ToDictionary(i => i.Name);
        Assert.Equal(("Formats code", "Acme", "2.5.1"), (by["fmt@acme-market"].Description, by["fmt@acme-market"].Author, by["fmt@acme-market"].Version));
        Assert.Equal(("", "Solo"), (by["solo@market"].Description, by["solo@market"].Author));
        Assert.Equal("shop", by["bare@shop"].Author);   // no manifest author: the marketplace
        Assert.Null(by["bare@shop"].Version);

        Write("skills/one/SKILL.md", "---\nname: one\ndescription: d\nmetadata:\n  author: meta-author\n---\n");
        Write("skills/two/SKILL.md", "---\nname: two\nauthor: top\n\n---\n");
        var rows = new List<Item>();
        Scanner.ReadRoot(Path.Combine(_tmp, "skills"), new ScanRoot(), "App", rows);
        Assert.Equal("meta-author", rows.Single(r => r.Name == "one").Author);
        Assert.Equal("top", rows.Single(r => r.Name == "two").Author);
    }
}
