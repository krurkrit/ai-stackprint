// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Reads an app's installed plugins or extensions. To support a new format: add a class and list it in <see cref="PluginReaders"/>;
/// an app names it in its "plugins" list (<see cref="PluginSource"/>) or, for Claude Code, "pluginFormat".
/// </summary>
public interface IPluginReader
{
    string Format { get; }
    void Read(string path, PluginContext ctx, List<Item> rows);
}

/// <summary>The app, level and project the plugins found belong to, and the source entry that named the path (null for pluginManifest).</summary>
public sealed record PluginContext(AppDefinition App, string Level, string? Project, PluginSource? Source);

public static class PluginReaders
{
    private static readonly IPluginReader[] _all =
        [new ClaudePluginReader(), new CodexPluginReader(), new FolderPluginReader(), new VsCodeExtensionReader(), new OpenCodeFileReader(),
         new OpenCodeConfigReader(), new GooseExtensionReader(), new EnabledListReader()];

    public static IPluginReader? Find(string? format) => format == null ? null : _all.FirstOrDefault(r => r.Format == format);

    /// <summary>Reads user-level plugins of <paramref name="app"/> at <paramref name="path"/>.</summary>
    public static void Read(this IPluginReader r, string path, AppDefinition app, List<Item> rows) => r.Read(path, new PluginContext(app, "User", null, null), rows);
}

/// <summary>~/.claude/plugins/installed_plugins.json: { "plugins": { "name@market": [ { scope, version, installPath } ] } }</summary>
internal class ClaudePluginReader : IPluginReader
{
    public string Format => "claude";

    public void Read(string path, PluginContext ctx, List<Item> rows)
    {
        var app = ctx.App;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("plugins", out var ps))
            {
                return;
            }

            foreach (var p in ps.EnumerateObject())
            {
                var entries = p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().ToList() : [p.Value];
                foreach (var e in entries)
                {
                    string Str(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                    var dir = Str("installPath");
                    var scope = Str("scope") is "" ? "user" : Str("scope");
                    var level = scope switch { "project" => "Project", "local" => "Local", "managed" => "System", _ => "User" };
                    var project = Str("projectPath") is "" ? null : Str("projectPath");
                    var m = Manifest(dir);
                    rows.Add(new Item("Plugin", p.Name, m.Description, app.Name, level, dir)
                    {
                        App = app.Id,
                        Scope = scope,
                        Project = project,
                        Version = Str("version") is "" ? null : Str("version"),
                        Author = m.Author ?? Market(p.Name),
                        Keywords = m.Keywords,
                        Provides = m.Provides
                    });
                    if (dir != "")
                    {
                        Scanner.Tagged(rows, () => Scanner.ReadRoot(dir, new ScanRoot
                        {
                            Level = level,
                            Depth = 6
                        }, $"{app.Name} · {p.Name.Split('@')[0]}", rows),
                            i => i with
                            {
                                App = app.Id,
                                Plugin = p.Name,
                                Project = project
                            });
                        PluginContents.Add(rows, dir, m, new(app, level, project, p.Name));
                    }
                }
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
    }

    /// <summary>What installPath/.claude-plugin/plugin.json says, and what the plugin adds (<see cref="PluginParts"/>).</summary>
    public static PluginManifest Manifest(string installPath) => PluginManifests.Read(installPath, [".claude-plugin/plugin.json"]);

    /// <summary>The marketplace part of name@marketplace: the owner when the manifest names no author.</summary>
    private static string? Market(string id) => id.Contains('@') ? id[(id.IndexOf('@') + 1)..] : null;
}
