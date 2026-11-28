// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;

using AiStackprint.Models;
using AiStackprint.Services;

namespace AiStackprint.App.Tests;

/// <summary>
/// A made-up home folder with files of a few AI tools and one registered project: the whole window can scan, show and edit them without touching this machine.
/// Everything under "~" (the tools' folders and ~/.ai-stackprint) follows it while it lives.
/// </summary>
internal sealed class FakeHome : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("aihome-").FullName;
    public string Project { get; }

    public FakeHome()
    {
        PathUtil.UseHome(Root);
        Write(".claude/skills/pdf-tools/SKILL.md", "---\nname: pdf-tools\ndescription: Read and fill PDF forms\n---\n# PDF tools\nUse it for PDFs.\n");
        Write(".claude/skills/review/SKILL.md", "---\nname: review\ndescription: Review a change\nallowed-tools: Bash\n---\nReview carefully.\n");
        Write(".claude/commands/ship.md", "---\ndescription: Ship it\n---\nRun the release.\n");
        Write(".claude/agents/reviewer.md", "---\nname: reviewer\ndescription: Reviews code\n---\nYou review code.\n");
        Write(".claude/settings.json", """{ "model": "claude-opus-5-5", "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo done" } ] } ] }, "permissions": { "allow": ["Bash(git status)"], "deny": ["Read(.env)"] } }""");
        Write(".claude.json", """{ "mcpServers": { "fs-server": { "command": "npx", "args": ["-y", "fs@1.0.0"] } } }""");
        Write(".claude/CLAUDE.md", "# Rules\nBe brief.\n");
        Write(".gemini/settings.json", """{ "theme": "dark" }""");
        Write(".codex/config.toml", "model = \"gpt-6\"\n");
        Project = Path.Combine(Root, "work", "demo");
        Write("work/demo/AGENTS.md", "# Demo\nUse tabs.\n");
        Write("work/demo/.claude/skills/deploy/SKILL.md", "---\nname: deploy\ndescription: Deploy the demo\n---\nDeploy.\n");
        var at = DateTime.UtcNow.AddHours(-1);   // relative: a fixed date falls out of the Usage card's 7 days
        Write(".claude/projects/-work-demo/session.jsonl", Lines(
            Event("user", at.ToString("O", CultureInfo.InvariantCulture), Msg("user", "Hello")),
            Event("assistant", at.AddSeconds(5).ToString("O", CultureInfo.InvariantCulture), Msg("assistant", new object[] { Text("Hi there"), Use("t1", "Read", new { file_path = "a.txt" }) }, usage: new
            {
                input_tokens = 100,
                output_tokens = 50,
                cache_read_input_tokens = 10,
                cache_creation_input_tokens = 5
            }))));
        Write(".claude/projects/-work-demo/session2.jsonl", Busy());
        ProjectStore.Save([new SavedProject(Project, DateTime.UtcNow)], ProjectStore.File, Root);
    }

    private static string Lines(params object[] events) => string.Join('\n', events.Select(e => JsonSerializer.Serialize(e))) + "\n";

    private static object Msg(string role, object content, string? id = null, object? usage = null) => new
    {
        id,
        role,
        model = role == "assistant" ? "claude-opus-5-5" : null,
        content,
        usage
    };

    private static object Event(string type, string time, object message, bool side = false, string? agent = null) => new
    {
        type,
        timestamp = time,
        isSidechain = side ? true : (bool?)null,
        agentId = agent,
        message
    };

    private static object Text(string text) => new
    {
        type = "text",
        text
    };

    private static object Use(string id, string name, object input) => new
    {
        type = "tool_use",
        id,
        name,
        input
    };

    private static object Result(string id, string content) => new
    {
        type = "tool_result",
        tool_use_id = id,
        content
    };

    /// <summary>A longer session: a summary, thinking, markdown, tool calls and their results (JSON, colored text, a long output, code), and two sub-agents.</summary>
    private static string Busy() => Lines(
        new
        {
            type = "summary",
            summary = "Refactor the parser"
        },
        Event("user", "2026-11-19T09:00:00Z", Msg("user", "Please refactor the parser and run the tests")),
        Event("assistant", "2026-11-19T09:00:05Z", Msg("assistant",
            new object[]
            {
                new
                {
                    type = "thinking",
                    thinking = "I should read the file first."
                },
                Text("# Plan\n\n- read the file\n- **change** it\n\n```csharp\nvar x = 1;\n```\nDone with `code`."),
                Use("t1", "Bash", new
                {
                    command = "dotnet test",
                    description = "Run tests"
                }),
                Use("t2", "Read", new { file_path = "src/Parser.cs" }),
            }, "m1", new
            {
                input_tokens = 200,
                output_tokens = 80,
                cache_read_input_tokens = 1000,
                cache_creation_input_tokens = 50
            })),
        Event("user", "2026-11-19T09:00:10Z", Msg("user", new object[]
        {
            Result("t1", "\u001b[32mPassed\u001b[0m 12 tests\n{\"ok\":true,\"count\":12}"),
            Result("t2", "public class Parser\n{\n    public int Parse() => 1;\n}\n"),
        })),
        Event("assistant", "2026-11-19T09:00:20Z", Msg("assistant", new object[] { Use("t3", "Task", new
        {
            description = "Search",
            prompt = "find usages"
        }), Use("t4", "Grep", new { pattern = "Parse" }) }, "m2", new
        {
            input_tokens = 300,
            output_tokens = 20
        })),
        Event("user", "2026-11-19T09:00:21Z", Msg("user", "find usages of Parse"), side: true, agent: "agent-1"),
        Event("assistant", "2026-11-19T09:00:22Z", Msg("assistant", new object[] { Text("Searching"), Use("s-t1", "Grep", new { pattern = "Parse" }) }, "s1", new
        {
            input_tokens = 50,
            output_tokens = 10
        }), side: true, agent: "agent-1"),
        Event("user", "2026-11-19T09:00:23Z", Msg("user", new object[] { Result("s-t1", "a.cs:3: Parse()") }), side: true, agent: "agent-1"),
        Event("assistant", "2026-11-19T09:00:30Z", Msg("assistant", new object[] { Text("Second agent here") }, "s2", new
        {
            input_tokens = 10,
            output_tokens = 5
        }), side: true, agent: "agent-2"),
        Event("user", "2026-11-19T09:00:40Z", Msg("user", new object[] { Result("t4", string.Join('\n', Enumerable.Range(1, 60).Select(i => "line " + i + " of a long result"))) })),
        Event("assistant", "2026-11-19T09:00:50Z", Msg("assistant", new object[] { Text("All done.") }, "m3", new
        {
            input_tokens = 400,
            output_tokens = 30
        })));

    public void Write(string rel, string text)
    {
        var path = Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        PathUtil.UseHome(null);
        try { Directory.Delete(Root, true); }
        catch (IOException) { }   // something still open: the temp folder is cleaned later
    }
}
