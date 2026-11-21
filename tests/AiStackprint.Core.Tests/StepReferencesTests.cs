// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>What a step points at: model, skill, MCP server, file.</summary>
public sealed class StepReferencesTests
{
    private static LogEntry Tool(string tool, string? detail, string? lang = ".json") => new("tool", "x", null, false, tool, lang, detail);

    [Fact]
    public void A_reply_points_at_its_model()
    {
        var e = new LogEntry("assistant", "hi", null, false) { Usage = new(DateTime.Now, "claude-opus-5-5", "", 1, 1, 0, 0, 0, false) };
        Assert.Equal([new StepRef("model", "claude-opus-5-5")], StepReferences.Find(e));
    }

    [Fact]
    public void A_skill_call_points_at_the_skill_and_an_mcp_call_at_its_server()
    {
        Assert.Equal([new StepRef("skill", "pdf-tools")], StepReferences.Find(Tool("Skill", "{ \"skill\": \"/pdf-tools\", \"args\": \"x\" }")));
        Assert.Equal([new StepRef("mcp", "github")], StepReferences.Find(Tool("mcp__github__create_issue", "{ \"title\": \"t\" }")));
    }

    [Fact]
    public void A_file_tool_points_at_its_file_whether_it_is_a_diff_or_a_plain_input()
    {
        Assert.Equal([new StepRef("file", "src/A.cs")], StepReferences.Find(Tool("Edit", "--- src/A.cs\n+++ src/A.cs\n@@\n-1\n+2\n", ".diff")));
        Assert.Equal([new StepRef("file", "b.txt")], StepReferences.Find(Tool("Read", "{ \"file_path\": \"b.txt\" }")));
    }

    [Fact]
    public void Other_steps_and_unreadable_input_point_nowhere()
    {
        Assert.Empty(StepReferences.Find(Tool("Bash", "ls")));
        Assert.Empty(StepReferences.Find(Tool("Skill", "not json")));
        Assert.Empty(StepReferences.Find(new LogEntry("user", "x", null, false)));
    }
}
