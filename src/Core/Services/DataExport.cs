// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>The parts of your data that can be exported and imported.</summary>
[Flags]
public enum DataParts
{
    None = 0,
    Settings = 1,
    Models = 2,
    Apps = 4,
    Projects = 8,
    All = Settings | Models | Apps | Projects,
}

/// <summary>The files export reads and import writes; tests use a temp folder.</summary>
public sealed record DataFiles(string Settings, string Models, string Apps, string Projects, string Backups, string Home)
{
    public static DataFiles Mine => new(SettingsStore.File, ModelCatalog.OverridesFile, AppCatalog.UserFile, ProjectStore.File, DataFolder.Backups, PathUtil.Home);
}

/// <summary>
/// One file with your settings, model changes, custom apps and project folders (<c>ai-stackprint-export.json</c>): only your changes, or every value.
/// Never state, caches, logs or anything from the apps' own files; paths under your home folder are written as <c>~/…</c>.
/// </summary>
public static class DataExport
{
    public const string FileName = "ai-stackprint-export.json";

    /// <summary>The export format's version; an import refuses a newer one.</summary>
    public const int Version = 1;

    /// <param name="allValues">Every value (settings with their defaults, models with the model data) instead of only your changes.</param>
    /// <param name="modelSource">The model data before your changes; null: the data in use.</param>
    public static JsonObject Build(DataParts parts, bool allValues, DataFiles f, DateOnly today, JsonObject? modelSource = null)
    {
        var o = new JsonObject { ["version"] = Version, ["exported"] = today.ToString("yyyy-MM-dd"), ["values"] = allValues ? "all" : "changes" };
        if (parts.HasFlag(DataParts.Settings))
        {
            var mine = MySettings(f.Settings);
            o["settings"] = allValues ? JsonLayers.Merge(SettingsStore.DefaultJson(), mine) : mine;
        }

        if (parts.HasFlag(DataParts.Models))
        {
            var mine = ModelCatalog.ReadOverrides(f.Models) ?? [];
            if (allValues)
            {
                var all = (JsonObject)(modelSource ?? ModelCatalog.SourceModels()).DeepClone();
                foreach (var (k, v) in mine)
                {
                    all[k] = all[k] is JsonObject theirs && v is JsonObject yours ? JsonLayers.Merge(theirs, yours) : v?.DeepClone();
                }
                mine = all;
            }
            o["models"] = mine.DeepClone();
        }

        if (parts.HasFlag(DataParts.Apps))
        {
            o["apps"] = JsonSerializer.SerializeToNode(AppCatalog.Read(f.Apps).Defs.Where(d => d.Origin == AppOrigin.Custom).ToList(), AppCatalog._opt);
        }

        if (parts.HasFlag(DataParts.Projects))
        {
            o["projects"] = new JsonArray([.. ProjectStore.Load(f.Projects, f.Home).Select(p => (JsonNode)new JsonObject
            {
                ["path"] = ProjectStore.ToFile(p.Path, f.Home),
                ["added"] = p.Added.ToUniversalTime().ToString("O"),
            })]);
        }

        return o;
    }

    /// <summary>Writes the export (indented) through a temp file.</summary>
    public static void Write(string path, JsonObject export)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, export.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.Move(tmp, path, true);
    }

    /// <summary>Your settings that differ from the defaults (what settings.json holds).</summary>
    internal static JsonObject MySettings(string file)
    {
        var all = (JsonObject)JsonSerializer.SerializeToNode(SettingsStore.Load(file), AppCatalog._opt)!;
        return JsonLayers.Diff(all, SettingsStore.DefaultJson());
    }
}
