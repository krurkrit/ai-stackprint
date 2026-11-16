// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class PluginReadersTests : IDisposable
{
    private static readonly AppDefinition _app = new() { Id = "app", Name = "App" };
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiprof-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private void Write(string rel, string text = "x")
    {
        var f = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, text);
    }

    private List<Item> Read(string format, string rel, PluginSource? src = null)
    {
        var rows = new List<Item>();
        PluginReaders.Find(format)!.Read(Path.Combine(_tmp, rel), new PluginContext(_app, "User", null, src), rows);
        return rows;
    }

    [Fact]
    public void Codex_lists_config_plugins_from_the_newest_cached_version()
    {
        Write(".codex/config.toml", "model = \"x\"\n[plugins.\"github@openai-curated\"]\nenabled = true\n\n[plugins.\"lint@acme\"]\nenabled = false\n[other]\nenabled = true\n");
        Write(".codex/plugins/cache/openai-curated/github/1.2.0/.codex-plugin/plugin.json", """{ "name": "github", "description": "old" }""");
        Write(".codex/plugins/cache/openai-curated/github/1.10.0/.codex-plugin/plugin.json",
            """{ "name": "github", "version": "1.10.0", "interface": { "displayName": "GitHub", "shortDescription": "Repos and <b>PRs</b>" }, "skills": "./skills/" }""");
        Write(".codex/plugins/cache/openai-curated/github/1.10.0/skills/review/SKILL.md", "---\nname: review\ndescription: d\n---\n");
        var rows = Read("codex", ".codex/config.toml");

        var github = rows.Single(r => r.Name == "github@openai-curated");
        Assert.Equal(("Repos and PRs", "1.10.0", true, (string?)null), (github.Description, github.Version, github.Bundled, github.Scope));
        Assert.Contains("skills", github.Provides);
        Assert.Equal("github@openai-curated", rows.Single(r => r.Kind == "Skill").Plugin);
        var lint = rows.Single(r => r.Name == "lint@acme");   // not in the cache: still listed, from the config
        Assert.Equal(("disabled", false), (lint.Scope, lint.Bundled));
        Assert.Equal(2, rows.Count(r => r.Kind == "Plugin"));
    }

    [Fact]
    public void Folders_read_each_manifest_kind_and_keep_one_copy_of_a_plugin()
    {
        Write("ext/conductor/gemini-extension.json", """{ "name": "conductor", "version": "0.2.0", "description": "Plans", "mcpServers": { "a": {} } }""");
        Write("ext/conductor/commands/plan.toml", "prompt = \"x\"");
        Write("ext/zed-py/extension.toml", "id = \"python\"\nname = \"Python\"\nversion = \"0.1.0\"\nauthors = [\"Jane <j@x.org>\"]\ndescription = \"Python support\"\n[grammars.python]\nrepository = \"x\"\n");
        Write("ext/hermes/plugin.yaml", "name: notes\nversion: 1.0\ndescription: |\n  long text\nauthor: Ann\n");
        Write("ext/mcpb/manifest.json", """{ "name": "fs", "display_name": "Filesystem", "author": { "name": "Anthropic" }, "server": { "type": "node" } }""");
        Write("ext/.hidden/plugin.json", """{ "name": "hidden" }""");
        Write("ext/market/conductor/gemini-extension.json", """{ "name": "conductor", "version": "0.1.9", "description": "older copy" }""");
        var all = Read("folders", "ext", new PluginSource { Depth = 2, Label = "Extensions" });
        Assert.Equal(("plan", "conductor"), Assert.Single(all, r => r.Kind == "Command") is var c ? (c.Name, c.Plugin) : default);   // the extension's command is a row of it
        var rows = all.Where(r => r.Kind == "Plugin").ToDictionary(r => r.Name);

        Assert.Equal(["Filesystem", "Python", "conductor", "notes"], rows.Keys.Order());
        Assert.Equal(("Plans", "0.2.0", "Extensions"), (rows["conductor"].Description, rows["conductor"].Version, rows["conductor"].LevelLabel));
        Assert.Equal(["commands", "mcp"], rows["conductor"].Provides);
        Assert.Equal(("Python support", "Jane", "0.1.0"), (rows["Python"].Description, rows["Python"].Author, rows["Python"].Version));
        Assert.Equal(("", "Ann"), (rows["notes"].Description, rows["notes"].Author));   // block values are not read
        Assert.Equal("Anthropic", rows["Filesystem"].Author);
        Assert.Equal(["mcp"], rows["Filesystem"].Provides);
    }

    [Fact]
    public void Vscode_extensions_skip_obsolete_folders_and_older_versions()
    {
        Write("exts/acme.lint-1.2.0/package.json", """{ "name": "lint", "displayName": "%name%", "publisher": "acme", "version": "1.2.0", "description": "old" }""");
        Write("exts/acme.lint-1.10.0-win32-x64/package.json", """{ "name": "lint", "displayName": "%name%", "publisher": "acme", "version": "1.10.0", "description": "%desc%" }""");
        Write("exts/acme.lint-1.10.0-win32-x64/package.nls.json", """{ "name": "Acme Lint", "desc": "Lints <i>code</i>" }""");
        Write("exts/gone.tool-0.1.0/package.json", """{ "name": "tool" }""");
        Write("exts/.obsolete", """{ "gone.tool-0.1.0": true }""");
        var p = Read("vscode", "exts").Single();
        Assert.Equal(("Acme Lint", "Lints code", "acme", "1.10.0"), (p.Name, p.Description, p.Author, p.Version));
    }

    [Fact]
    public void Opencode_reads_plugin_files_and_npm_packages_in_the_config()
    {
        Write("oc/plugins/notify.js", "export default {}");
        Write("oc/plugins/readme.md");
        Write("oc/plugins/status-tui/package.json", """{ "name": "status-tui", "version": "0.3.0" }""");
        Assert.Equal(["notify", "status-tui"], Read("opencode-files", "oc/plugins").Select(r => r.Name).Order());

        Write("oc/opencode.jsonc", """{ /* c */ "plugin": ["opencode-wakatime", "@acme/helper@1.2.0", { "package": "x@2" }, 3], "apiKey": "never-read", }""");
        var npm = Read("opencode-config", "oc/opencode.jsonc");
        Assert.Equal(["opencode-wakatime", "@acme/helper", "x"], npm.Select(r => r.Name));
        Assert.Equal([null, "1.2.0", "2"], npm.Select(r => r.Version));
        Assert.DoesNotContain(npm, r => r.Description.Contains("never-read"));
    }

    [Fact]
    public void Goose_reads_the_extensions_block_only()
    {
        Write("goose/config.yaml", """
            GOOSE_PROVIDER: openai
            extensions:
              developer:
                bundled: true
                display_name: Developer
                enabled: true
                name: developer
                type: builtin
              github:
                enabled: false
                name: github
                description: GitHub <b>tools</b>
                type: stdio
                cmd: npx
            other:
              name: not-an-extension
            """);
        var rows = Read("goose", "goose/config.yaml").ToDictionary(r => r.Name);
        Assert.Equal(["Developer", "github"], rows.Keys);
        Assert.Equal((true, (string?)null, 0), (rows["Developer"].Bundled, rows["Developer"].Scope, rows["Developer"].Provides.Count));
        Assert.Equal(("disabled", "GitHub tools"), (rows["github"].Scope, rows["github"].Description));
        Assert.Equal(["mcp"], rows["github"].Provides);
    }

    [Fact]
    public void Enabled_list_reads_enabled_plugins()
    {
        Write("f/settings.json", """{ "enabledPlugins": { "core@factory": true, "old@factory": false }, "token": "x" }""");
        var rows = Read("enabled-list", "f/settings.json");
        Assert.Equal([("core@factory", null), ("old@factory", "disabled")], rows.Select(r => (r.Name, r.Scope)));
    }

    [Fact]
    public void Missing_or_broken_sources_add_nothing()
    {
        Write("bad.json", "{ not json");
        foreach (var f in new[] { "codex", "folders", "vscode", "opencode-files", "opencode-config", "goose", "enabled-list" })
        {
            Assert.Empty(Read(f, "missing"));
        }

        Assert.Empty(Read("opencode-config", "bad.json"));
        Assert.Empty(Read("enabled-list", "bad.json"));
    }

    [Fact]
    public void Every_builtin_plugin_source_names_a_known_reader()
    {
        var defs = AppCatalog.BuiltIn();
        Assert.All(defs.SelectMany(d => d.Plugins), s => Assert.NotNull(PluginReaders.Find(s.Format)));
        Assert.True(defs.Count(d => d.Plugins.Count > 0 || d.PluginManifest != null) >= 15);
    }
}
