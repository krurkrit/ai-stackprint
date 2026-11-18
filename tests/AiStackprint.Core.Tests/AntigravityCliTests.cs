// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class AntigravityCliTests
{
    [Fact]
    public void Antigravity_CLI_is_found_by_its_own_folder_the_shared_config_folder_or_the_launcher_folder_and_its_files_are_listed()
    {
        var d = AppCatalog.Read().Defs.Single(a => a.Id == "antigravity-cli");
        Assert.Equal("agent", d.Category);
        Assert.Contains("~/.gemini/antigravity-cli", d.Detect);
        Assert.Contains("~/.gemini/config", d.Detect);   // where the CLI keeps mcp_config.json, plugins and projects
        Assert.Contains("%APPDATA%/Antigravity/bin", d.Detect);   // the agy launcher the Antigravity app installs on Windows
        Assert.All(d.Detect, p => Assert.False(p.EndsWith(".cmd", StringComparison.Ordinal) || p.EndsWith(".exe", StringComparison.Ordinal), $"{p}: detection looks for folders"));
        Assert.Contains(d.Roots, r => r.Path == "~/.gemini/config/skills");
        Assert.Contains(d.ProjectRoots, r => r.Path == ".agents/skills");
        Assert.Contains(d.Configs, c => c.Path == "~/.gemini/config/mcp_config.json" && c.Kind == "mcp");
        Assert.Contains(d.Logs, l => l.Pattern == "transcript.jsonl");
    }

    [Fact]
    public void An_app_with_one_of_its_folders_on_disk_is_installed_and_listed_with_its_instruction_and_mcp_files()
    {
        var home = Directory.CreateTempSubdirectory("agy-home").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(home, ".gemini", "config"));
            File.WriteAllText(Path.Combine(home, ".gemini", "config", "mcp_config.json"), "{}");
            var def = new AiStackprint.Models.AppDefinition { Id = "agy-test", Name = "Agy test", Category = "agent" };
            def.Detect.Add(Path.Combine(home, ".gemini", "antigravity-cli"));
            def.Detect.Add(Path.Combine(home, ".gemini", "config"));
            var r = Scanner.Collect([def], []);
            var app = Assert.Single(r.Apps);
            Assert.True(app.Installed);
            Assert.EndsWith("config", app.Folder);
        }
        finally { Directory.Delete(home, true); }
    }
}
