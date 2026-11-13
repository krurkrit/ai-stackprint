using System.Text.Json;
using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Loads and saves <see cref="Settings"/> as settings.json in the app data folder.</summary>
public static class SettingsStore
{
    public static string File => Path.Combine(AppCatalog.Folder, "settings.json");

    public static Settings Load(string? file = null)
    {
        file ??= File;
        try { return System.IO.File.Exists(file) ? JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(file), AppCatalog.Opt) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { return new(); }
    }

    public static void Save(Settings s, string? file = null)
    {
        file ??= File;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        System.IO.File.WriteAllText(file, JsonSerializer.Serialize(s, AppCatalog.Opt));
    }

    /// <summary>Apps to scan: everything except the ones hidden in settings.</summary>
    public static List<AppDefinition> Visible(IEnumerable<AppDefinition> defs, Settings s) =>
        defs.Where(d => !s.HiddenApps.Contains(d.Id)).ToList();
}
