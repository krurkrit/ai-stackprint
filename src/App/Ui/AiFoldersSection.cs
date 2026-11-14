// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Project page: the AI folders in the project (.claude, .cursor …) with what is inside, and the commands and agents that apply here.</summary>
internal static class AiFoldersSection
{
    private static readonly (string Key, string One, string Many)[] _words =
    [
        ("Command", "command", "commands"), ("Agent", "agent", "agents"), ("Skill", "skill", "skills"), ("Plugin", "plugin", "plugins"),
        ("Settings", "settings file", "settings files"), ("MCP servers", "MCP file", "MCP files"), ("Instructions", "instruction file", "instruction files"), ("Ignore", "ignore file", "ignore files")
    ];

    /// <summary>"3 commands · 1 agent · 4 other files".</summary>
    public static string Inside(AiFolder f)
    {
        var parts = _words.Where(w => f.Counts.GetValueOrDefault(w.Key) > 0).Select(w => f.Counts[w.Key] == 1 ? $"1 {w.One}" : $"{f.Counts[w.Key]} {w.Many}").ToList();
        var other = f.Files - f.Counts.Values.Sum();
        if (other > 0)
        {
            parts.Add($"{other}{(f.Capped ? "+" : "")} other");
        }

        return parts.Count == 0 ? "Empty" : string.Join(" · ", parts);
    }

    public static Control Build(Ctx x, string project, List<Item> applying)
    {
        var defs = x.S.Defs.Where(d => x.ShownApps.Any(a => a.Id == d.Id)).ToList();
        var folders = AiFolders.List(project, defs, x.S.Items);
        var rows = folders.Select(f => new TableRow(
        [
            Mono(f.Name, TextSize.Small), Txt(string.Join(", ", f.Apps), TextSize.Small, false, Muted), Txt(Inside(f), TextSize.Small),
            Txt(f.Capped ? $"{f.Files}+" : f.Files.ToString(), TextSize.Small), Txt(LogViewer.Size(f.Bytes), TextSize.Small, false, Muted),
            IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(f.Path))
        ]));
        var table = Table.Build([new Col("Folder", "1*"), new Col("Apps", "2*"), new Col("Inside", "3*"), new Col("Files", ".6*"), new Col("Size", ".8*"), new Col("", "40")], rows, "No AI folders in this project.");

        var parts = applying.Where(i => i.Kind is "Command" or "Agent").OrderBy(i => Array.IndexOf(Levels.Order, i.Level)).ThenBy(i => i.Kind).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => new TableRow(
            [
                Txt(i.Name, TextSize.Body, true), Txt(i.Kind, TextSize.Small), Txt(i.Source, TextSize.Small), AppDetailView.LevelCell(i), Txt(i.Description, TextSize.Small, false, Muted),
                IconBtn("open", "Open file", () => PathUtil.OpenFile(i.Path))
            ])).ToList();
        var list = Table.Build([new Col("Name", "1.4*"), new Col("Type", ".8*"), new Col("App", "1.2*"), new Col("Level", "1.2*"), new Col("Description", "3*"), new Col("", "40")], parts, "No commands or agents apply here.");

        return new StackPanel
        {
            Spacing = Space.XL,
            Children = { Section("folder", "AI folders", table), Section("sparkle", $"Commands and agents · {parts.Count}", list) }
        };
    }
}
