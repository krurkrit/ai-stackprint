// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Writing an import: backups first, then each part merged into your files.</summary>
public static partial class DataImport
{
    /// <summary>
    /// Writes <paramref name="plan"/>: each file it changes is copied to <c>backups/import-&lt;time&gt;/</c> first. <paramref name="moved"/> gives a new folder for a
    /// missing project (one not in it is skipped). Returns the backup folder, or null when nothing was written.
    /// </summary>
    public static string? Apply(ImportPlan plan, DataFiles f, IReadOnlyDictionary<string, string> moved, DateTime now, JsonObject? modelSource = null)
    {
        var parts = plan.Changes.Select(c => c.Part).Aggregate(DataParts.None, (a, b) => a | b) | (moved.Count > 0 ? DataParts.Projects : DataParts.None);
        if (parts == DataParts.None)
        {
            return null;
        }

        var backup = Path.Combine(f.Backups, $"import-{now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(backup);
        foreach (var (part, file) in new[] { (DataParts.Settings, f.Settings), (DataParts.Models, f.Models), (DataParts.Apps, f.Apps), (DataParts.Projects, f.Projects) })
        {
            if (parts.HasFlag(part) && File.Exists(file))
            {
                File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true);
            }
        }

        var data = plan.Data;
        if (parts.HasFlag(DataParts.Settings) && data["settings"] is JsonObject settings)
        {
            var mine = DataExport.MySettings(f.Settings);
            foreach (var (k, v) in settings.Where(s => !Consents.Contains(s.Key)))
            {
                mine[k] = v?.DeepClone();
            }

            var all = JsonLayers.Merge(SettingsStore.DefaultJson(), mine);
            SettingsStore.Save(all.Deserialize<Settings>(AppCatalog._opt)!, f.Settings);   // only values that differ from the defaults are written
        }

        if (parts.HasFlag(DataParts.Models) && data["models"] is JsonObject models)
        {
            var source = modelSource ?? ModelCatalog.SourceModels();
            var mine = ModelCatalog.ReadOverrides(f.Models) ?? [];
            foreach (var (key, node) in models)
            {
                if (node is not JsonObject theirs)
                {
                    continue;
                }

                var used = JsonLayers.Merge(Used(source[key] as JsonObject, mine[key] as JsonObject), theirs);
                var yours = source[key] is JsonObject data0 ? JsonLayers.Diff(used, data0) : used;   // values equal to the model data here are not yours
                if (yours.Count == 0)
                {
                    mine.Remove(key);
                }
                else
                {
                    mine[key] = yours;
                }
            }

            if (ModelCatalog.WriteOverrides(mine, f.Models) is { } why)
            {
                throw new IOException(why);
            }
        }

        if (parts.HasFlag(DataParts.Apps) && data["apps"] is JsonArray apps)
        {
            foreach (var def in Apps(apps).Where(d => AppCatalog.Refuse(d) == null))
            {
                AppCatalog.SaveUser(def, f.Apps);
            }
        }

        if (parts.HasFlag(DataParts.Projects) && data["projects"] is JsonArray projects)
        {
            var list = ProjectStore.Load(f.Projects, f.Home);
            foreach (var p in Projects(projects, f.Home))
            {
                var path = Directory.Exists(p.Path) ? p.Path : moved.GetValueOrDefault(p.Path);
                if (path != null && Directory.Exists(path) && !list.Exists(h => ProjectStore.Same(h.Path, path)))
                {
                    list.Add(p with { Path = path });
                }
            }

            ProjectStore.Save(list, f.Projects, f.Home);
        }

        return backup;
    }
}
