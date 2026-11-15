// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// settings.json in the data folder (<see cref="DataFolder"/>) holds only the settings you changed: loading puts them over the built-in
/// default/settings.json, saving writes only what differs from it. Format and versions: <see cref="VersionedJson"/>.
/// </summary>
public static class SettingsStore
{
    private static readonly VersionedJson _format = new("settings", 1);
    private static readonly Lazy<JsonObject> _defaults = new(ReadDefaults);

    /// <summary>Keys older versions kept in settings.json that now live in state.json.</summary>
    private static readonly string[] _stateKeys = ["panelHeight", "updateCheckedUtc", "updateDismissed"];

    public static string File => Path.Combine(DataFolder.Root, "settings.json");

    /// <summary>Shown once when settings.json comes from a newer AI Stackprint: changes here are not saved, so going back to it loses nothing.</summary>
    public static string? Problem => _format.IsNewer(File)
        ? "settings.json comes from a newer AI Stackprint: settings you change here are used but not saved."
        : null;

    /// <summary>A new set of the default settings (default/settings.json).</summary>
    public static Settings Defaults() => Deserialize(_defaults.Value);

    public static Settings Load(string? file = null)
    {
        file ??= File;
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        JsonObject? mine;
        try { mine = _format.Read(file, Path.Combine(folder, "backups")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { mine = null; }

        if (mine == null)
        {
            return Defaults();
        }

        var legacy = new JsonObject();
        foreach (var k in _stateKeys)
        {
            if (mine.Remove(k, out var v))
            {
                legacy[k] = v;
            }
        }

        if (legacy.Count > 0)
        {
            StateStore.Adopt(legacy, Path.Combine(folder, StateStore.FileName));
        }

        try { return Deserialize(JsonLayers.Merge(_defaults.Value, mine)); }
        catch (JsonException) { return Defaults(); }   // a value of the wrong type: the file stays as it is until a save
    }

    public static void Save(Settings s, string? file = null)
    {
        var all = (JsonObject)JsonSerializer.SerializeToNode(s, AppCatalog._opt)!;
        _format.Write(file ?? File, JsonLayers.Diff(all, _defaults.Value));
    }

    /// <summary>Apps to scan: everything except the ones hidden in settings.</summary>
    public static List<AppDefinition> Visible(IEnumerable<AppDefinition> defs, Settings s) =>
        [.. defs.Where(d => !s.HiddenApps.Contains(d.Id))];

    /// <summary>default/settings.json without its version (data, not a setting).</summary>
    internal static JsonObject DefaultJson() => (JsonObject)_defaults.Value.DeepClone();

    private static Settings Deserialize(JsonObject o) => o.Deserialize<Settings>(AppCatalog._opt)!;

    private static JsonObject ReadDefaults()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("default/settings.json")!;
        var o = JsonNode.Parse(s, documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        o.Remove(VersionedJson.Key);
        return o;
    }
}
