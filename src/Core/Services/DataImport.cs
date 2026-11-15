// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>One value an import changes: <paramref name="What"/> is a setting, a model field, an app or a project folder.</summary>
public sealed record ImportChange(DataParts Part, string What, string? Before, string? After);

/// <summary>What an import would do, shown before anything is written.</summary>
/// <param name="AllValues">The file holds every value, not only the exporter's changes (explained in the preview).</param>
/// <param name="MissingProjects">Project folders in the file that do not exist here: locate each or skip it.</param>
/// <param name="Notes">What is left out and why (network consents, apps that are built in now).</param>
public sealed record ImportPlan(JsonObject Data, bool AllValues, List<ImportChange> Changes, List<string> MissingProjects, List<string> Notes);

/// <summary>The file cannot be imported (not an export, or from a newer AI Stackprint).</summary>
public sealed class ImportRefusedException(string message) : Exception(message);

/// <summary>
/// Imports an export (<see cref="DataExport"/>): a preview of every change first, then a backup of each file it writes to <c>backups/import-…</c>, then a merge:
/// imported values go over yours, and only values that differ from the defaults or the model data are saved. Consents to network calls are never imported.
/// </summary>
public static partial class DataImport
{
    /// <summary>Settings that turn on network calls: always left as they are here.</summary>
    public static readonly string[] Consents = ["limitsEnabled", "checkUpdates"];

    public static ImportPlan Plan(string json, DataFiles f, JsonObject? modelSource = null)
    {
        JsonObject data;
        try
        {
            data = JsonNode.Parse(json, documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new ImportRefusedException("This is not an AI Stackprint export.");
        }
        catch (JsonException e) { throw new ImportRefusedException($"This is not valid JSON (line {e.LineNumber + 1})."); }

        if (data["version"] is not JsonValue v || !v.TryGetValue(out int version) || data["exported"] == null)
        {
            throw new ImportRefusedException("This is not an AI Stackprint export.");
        }

        if (version > DataExport.Version)
        {
            throw new ImportRefusedException("This export comes from a newer AI Stackprint. Update this app to import it.");
        }

        var changes = new List<ImportChange>();
        var notes = new List<string>();
        var missing = new List<string>();
        if (data["settings"] is JsonObject settings)
        {
            var current = JsonLayers.Merge(SettingsStore.DefaultJson(), DataExport.MySettings(f.Settings));
            foreach (var (k, value) in settings)
            {
                if (Consents.Contains(k))
                {
                    notes.Add($"Settings: {k} is not imported (network calls stay as they are here; turn them on in Settings).");
                }
                else if (!JsonNode.DeepEquals(current[k], value))
                {
                    changes.Add(new(DataParts.Settings, k, Show(current[k]), Show(value)));
                }
            }
        }

        if (data["models"] is JsonObject models)
        {
            var source = modelSource ?? ModelCatalog.SourceModels();
            var mine = ModelCatalog.ReadOverrides(f.Models) ?? [];
            foreach (var (key, node) in models)
            {
                if (node is not JsonObject theirs)
                {
                    continue;
                }

                var now = Leaves(Used(source[key] as JsonObject, mine[key] as JsonObject));
                foreach (var (path, after) in Leaves(theirs))
                {
                    if (now.GetValueOrDefault(path) != after)
                    {
                        changes.Add(new(DataParts.Models, $"{key} · {path}", now.GetValueOrDefault(path), after));
                    }
                }
            }
        }

        if (data["apps"] is JsonArray apps)
        {
            var have = AppCatalog.Read(f.Apps);
            foreach (var def in Apps(apps))
            {
                if (AppCatalog.Refuse(def) != null)
                {
                    notes.Add($"Apps: {def.Name} is built in now: not imported.");
                }
                else if (have.Defs.Find(d => d.Id == def.Id) is not { } old || JsonSerializer.Serialize(old, AppCatalog._opt) != JsonSerializer.Serialize(def, AppCatalog._opt))
                {
                    changes.Add(new(DataParts.Apps, def.Name, have.Defs.Exists(d => d.Id == def.Id) ? "yours" : null, "imported"));
                }
            }
        }

        if (data["projects"] is JsonArray projects)
        {
            var have = ProjectStore.Load(f.Projects, f.Home);
            foreach (var p in Projects(projects, f.Home).Where(p => !have.Exists(h => ProjectStore.Same(h.Path, p.Path))))
            {
                if (Directory.Exists(p.Path))
                {
                    changes.Add(new(DataParts.Projects, p.Path, null, "added"));
                }
                else
                {
                    missing.Add(p.Path);
                }
            }
        }

        var all = data["values"] is JsonValue s && s.TryGetValue(out string? mode) && mode == "all";
        return new(data, all, changes, missing, notes);
    }

    private static JsonObject Used(JsonObject? theirs, JsonObject? yours) =>
        theirs != null && yours != null ? JsonLayers.Merge(theirs, yours) : (JsonObject?)(yours ?? theirs)?.DeepClone() ?? [];

    /// <summary>A model's values by path ("price.input" → "4"), for the preview.</summary>
    internal static Dictionary<string, string> Leaves(JsonObject o, string prefix = "")
    {
        var d = new Dictionary<string, string>();
        foreach (var (k, v) in o)
        {
            if (v is JsonObject inner)
            {
                foreach (var (p, x) in Leaves(inner, prefix + k + "."))
                {
                    d[p] = x;
                }
            }
            else if (v != null)
            {
                d[prefix + k] = Show(v)!;
            }
        }
        return d;
    }

    private static string? Show(JsonNode? n) => n switch
    {
        null => null,
        JsonValue v when v.TryGetValue(out string? s) => s,
        _ => n.ToJsonString(),
    };

    private static List<AppDefinition> Apps(JsonArray apps)
    {
        try { return apps.Deserialize<List<AppDefinition>>(AppCatalog._opt) ?? []; }
        catch (JsonException) { throw new ImportRefusedException("The apps in this export cannot be read."); }
    }

    private static IEnumerable<SavedProject> Projects(JsonArray projects, string home)
    {
        foreach (var p in projects.OfType<JsonObject>())
        {
            if (p["path"] is JsonValue v && v.TryGetValue(out string? path) && path.Length > 0)
            {
                var added = p["added"] is JsonValue a && a.TryGetValue(out string? s) && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : DateTime.UtcNow;
                yield return new(ProjectStore.FromFile(path, home), added);
            }
        }
    }
}
