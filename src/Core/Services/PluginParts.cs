// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>
/// What a plugin or extension adds to its app: skills, commands, agents, hooks, MCP servers, LSP servers. A part counts when the manifest
/// names it (plugin.json "skills", "commands", "agents", "hooks", "mcpServers", "lspServers"; an MCP bundle's "server") or its default
/// place in the plugin folder has something (Gemini and Qwen commands are .toml files). Best effort: other folders a plugin points at are not followed.
/// </summary>
public static class PluginParts
{
    /// <param name="Label">As shown: "MCP servers".</param>
    /// <param name="Key">What search uses (has:mcp) and what <see cref="Models.Item.Provides"/> holds.</param>
    /// <param name="Field">The manifest fields that name it, separated by |.</param>
    /// <param name="Place">Default place in the plugin folder: a folder (holding <paramref name="Files"/>, patterns separated by ;) or a file.</param>
    public sealed record Part(string Key, string Label, string Field, string Place, string? Files);

    public static readonly IReadOnlyList<Part> All =
    [
        new("skills", "Skills", "skills", "skills", "SKILL.md"),
        new("commands", "Commands", "commands", "commands", "*.md;*.toml"),
        new("agents", "Agents", "agents", "agents", "*.md"),
        new("hooks", "Hooks", "hooks", Path.Combine("hooks", "hooks.json"), null),
        new("mcp", "MCP servers", "mcpServers|server", ".mcp.json", null),
        new("lsp", "LSP servers", "lspServers", ".lsp.json", null),
    ];

    private static readonly EnumerationOptions _near = new() { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };

    public static string Name(string key) => All.FirstOrDefault(p => p.Key == key)?.Label ?? key;

    /// <summary>The keys of the parts the plugin at <paramref name="folder"/> adds, in the order of <see cref="All"/>.</summary>
    /// <param name="manifest">The root of its plugin.json, when it has one.</param>
    public static IReadOnlyList<string> Find(string folder, JsonElement? manifest)
    {
        if (folder == "")
        {
            return [];
        }

        return [.. All.Where(p => p.Field.Split('|').Any(f => Named(manifest, f)) || Present(folder, p)).Select(p => p.Key)];
    }

    private static bool Named(JsonElement? manifest, string field) =>
        manifest is { ValueKind: JsonValueKind.Object } m && m.TryGetProperty(field, out var v) && v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() != "",
            JsonValueKind.Array => v.GetArrayLength() > 0,
            JsonValueKind.Object => v.EnumerateObject().Any(),
            _ => false
        };

    private static bool Present(string folder, Part p)
    {
        var at = Path.Combine(folder, p.Place);
        try
        {
            return p.Files == null ? File.Exists(at) : Directory.Exists(at) && p.Files.Split(';').Any(f => Directory.EnumerateFiles(at, f, _near).Any());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
