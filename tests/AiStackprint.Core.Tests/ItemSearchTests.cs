// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class ItemSearchTests
{
    private static Item Skill(string name, string desc = "", string? author = null, string app = "claude-code", string source = "Claude Code", string level = "User") =>
        new("Skill", name, desc, source, level, $"/home/me/.claude/skills/{name}/SKILL.md")
        {
            App = app,
            Author = author
        };

    private static readonly Item[] _items =
    [
        Skill("pdf", "Read and fill PDF forms", "Anthropic"),
        Skill("pdf-tools", "Merge files", "Jane Doe"),
        Skill("code-review", "Review a pull request", "Anthropic"),
        Skill("my-pdf-export", "Export pages"),
        Skill("deploy", "Ship to staging; makes a PDF report", "Jane", app: "codex", source: "Codex", level: "Project"),
        new Item("Plugin", "fmt@acme", "Formats code", "Claude Code", "User", "/p/fmt")
        {
            App = "claude-code",
            Author = "Acme",
            Keywords = ["lint", "prettier"]
        },
    ];

    private static List<string> Find(string q) => new ItemSearch(_items.OrderBy(i => i.Name, StringComparer.Ordinal)).Find(q).Select(i => i.Name).ToList();

    [Fact]
    public void Empty_search_lists_everything_in_the_given_order()
    {
        Assert.Equal(["code-review", "deploy", "fmt@acme", "my-pdf-export", "pdf", "pdf-tools"], Find("  "));
    }

    [Fact]
    public void Words_match_separately_and_all_must_match()
    {
        Assert.Equal(["pdf"], Find("forms pdf"));
        Assert.Equal(["code-review"], Find("pull review"));
        Assert.Empty(Find("pdf pull"));
    }

    [Fact]
    public void Name_matches_rank_first_then_keywords_author_description_path()
    {
        // whole name, name start, start of a word in the name, then the description
        Assert.Equal(["pdf", "pdf-tools", "my-pdf-export", "deploy"], Find("pdf"));
        Assert.Equal(["fmt@acme"], Find("prettier"));       // keyword
        Assert.Equal(["fmt@acme"], Find("lin"));            // part of a keyword
        Assert.Equal(["deploy", "pdf-tools"], Find("jane"));   // author; equal ranks keep the name order
    }

    [Fact]
    public void Quoted_words_match_as_a_phrase()
    {
        Assert.Equal(["code-review"], Find("\"a pull\""));
        Assert.Empty(Find("\"pull a\""));
    }

    [Fact]
    public void Fields_match_only_that_field_and_quotes_match_the_whole_value()
    {
        Assert.Equal(["code-review", "pdf"], Find("author:anthropic"));
        Assert.Equal(["deploy", "pdf-tools"], Find("by:jane"));
        Assert.Equal(["deploy"], Find("author:\"Jane\""));         // not "Jane Doe"
        Assert.Empty(Find("app:codex"));   // no app: field: the App menu filters by app
        Assert.Equal(["deploy"], Find("level:project"));
        Assert.Equal(["fmt@acme"], Find("tag:lint"));
        Assert.Equal(["fmt@acme"], Find("keyword:\"prettier\""));
        Assert.Equal(["my-pdf-export", "pdf", "pdf-tools"], Find("name:pdf"));
        Assert.Equal(["deploy"], Find("desc:staging"));
    }

    [Fact]
    public void Unknown_fields_and_bare_fields_do_not_break_the_search()
    {
        Assert.Equal(Find(""), Find("author:"));      // still typing
        Assert.Empty(Find("https://example.com"));    // not a field: searched as text
        Assert.Equal(["fmt@acme"], Find("fmt@acme"));
    }

    [Fact]
    public void Author_of_a_plugins_skill_is_the_plugins_author()
    {
        var plugin = new Item("Plugin", "fmt@acme", "", "Claude Code", "User", "/p")
        {
            App = "claude-code",
            Author = "Acme"
        };
        var inside = Skill("format", source: "Claude Code · fmt") with { Plugin = "fmt@acme" };
        var market = Skill("other", source: "Claude Code · x") with { Plugin = "x@shop" };
        var bundled = Skill("sys", source: "Codex") with { Bundled = true };
        var mine = Skill("mine");
        var author = ItemAuthor.For([plugin, inside, market, bundled, mine]);
        Assert.Equal(["Acme", "shop", "Codex", ItemAuthor.Local], new[] { inside, market, bundled, mine }.Select(author));

        var search = new ItemSearch([inside, market, bundled, mine], author);
        Assert.Equal(["format"], search.Find("author:\"acme\"").Select(i => i.Name));
        Assert.Equal(["mine"], search.Find("author:\"Local\"").Select(i => i.Name));
    }

    [Fact]
    public void Spans_mark_every_match_and_join_overlaps()
    {
        Assert.Equal([(0, 3), (8, 3)], SearchQuery.Spans("PDF and pdf", ["pdf"]));
        Assert.Equal([(0, 9)], SearchQuery.Spans("pdf-tools", ["pdf-t", "tools"]));
        Assert.Empty(SearchQuery.Spans("abc", ["x", ""]));
    }

    [Fact]
    public void Marks_are_the_free_words_and_that_fields_values()
    {
        var q = SearchQuery.Parse("pdf author:jane name:tool \"fill forms\"");
        Assert.Equal(["pdf", "tool", "fill forms"], q.Marks("name"));
        Assert.Equal(["pdf", "fill forms"], q.Marks("desc"));
    }
}
