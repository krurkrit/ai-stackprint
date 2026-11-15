// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Loads and saves <see cref="Settings"/> as settings.json in the data folder (<see cref="DataFolder"/>).</summary>
public static class SettingsStore
{
    public static string File => Path.Combine(DataFolder.Root, "settings.json");

    public static Settings Load(string? file = null)
    {
        file ??= File;
        try { return System.IO.File.Exists(file) ? JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(file), AppCatalog._opt) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    public static void Save(Settings s, string? file = null)
    {
        file ??= File;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        System.IO.File.WriteAllText(file, JsonSerializer.Serialize(s, AppCatalog._opt));
    }

    /// <summary>Apps to scan: everything except the ones hidden in settings.</summary>
    public static List<AppDefinition> Visible(IEnumerable<AppDefinition> defs, Settings s) =>
        [.. defs.Where(d => !s.HiddenApps.Contains(d.Id))];
}
