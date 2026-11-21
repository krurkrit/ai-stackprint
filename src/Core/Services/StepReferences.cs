// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

/// <param name="Kind">model, skill, mcp or file.</param>
/// <param name="Value">The model name, the skill name, the MCP server name, or the file path.</param>
public sealed record StepRef(string Kind, string Value);

/// <summary>What a step points at: the model that answered, the skill it ran, the MCP server it called, the files it read or changed. The viewer turns each into a link to its page.</summary>
public static class StepReferences
{
    private static readonly string[] FileTools = ["Read", "Edit", "Write", "MultiEdit", "NotebookEdit"];

    public static List<StepRef> Find(LogEntry e)
    {
        var refs = new List<StepRef>();
        if (e.Usage is { Model: { Length: > 0 } model })
        {
            refs.Add(new("model", model));
        }

        if (e.Role != "tool" || e.Tool == null)
        {
            return refs;
        }

        if (e.Tool.StartsWith("mcp__", StringComparison.Ordinal) && e.Tool.Split("__") is { Length: > 2 } parts)
        {
            refs.Add(new("mcp", parts[1]));
        }

        var input = Input(e.Detail);
        if (string.Equals(e.Tool, "Skill", StringComparison.OrdinalIgnoreCase) && Str(input, "skill", "name") is { } skill)
        {
            refs.Add(new("skill", skill.TrimStart('/')));
        }

        if (e.Lang == ".diff" && e.Detail?.StartsWith("--- ", StringComparison.Ordinal) == true)
        {
            refs.Add(new("file", e.Detail[4..e.Detail.IndexOf('\n')].Trim()));
        }
        else if (FileTools.Contains(e.Tool) && Str(input, "file_path", "path", "notebook_path") is { } file)
        {
            refs.Add(new("file", file));
        }

        return refs;
    }

    private static JsonElement? Input(string? detail)
    {
        if (detail == null || !detail.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try { return JsonDocument.Parse(detail).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement? input, params string[] names)
    {
        if (input is not { ValueKind: JsonValueKind.Object } o)
        {
            return null;
        }

        foreach (var n in names)
        {
            if (o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
            {
                return v.GetString();
            }
        }

        return null;
    }
}
