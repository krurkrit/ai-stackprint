// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Loads built-in app definitions, then user definitions (same id overrides the built-in).</summary>
public static class AppCatalog
{
    internal static readonly JsonSerializerOptions _opt = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string Folder => Path.Combine(AppData, "AiStackprint");

    /// <summary>
    /// One-time move of data saved under the app's old name (AI Profiler): apps.json, settings.json, removed skills, backups.
    /// Only when the new folder does not exist yet, so it never overwrites anything. Returns true when it moved.
    /// </summary>
    public static bool MigrateOldFolder(string? appData = null)
    {
        appData ??= AppData;
        string old = Path.Combine(appData, "AiProfiler"), now = Path.Combine(appData, "AiStackprint");
        if (!Directory.Exists(old) || Directory.Exists(now))
        {
            return false;
        }

        Directory.Move(old, now);
        return true;
    }
    public static string UserFile => Path.Combine(Folder, "apps.json");

    /// <summary>The built-in app definitions only (apps.builtin.json), without the user's apps.json.</summary>
    public static List<AppDefinition> BuiltIn()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("apps.builtin.json")!;
        return JsonSerializer.Deserialize<List<AppDefinition>>(s, _opt) ?? [];
    }

    public static List<AppDefinition> Load()
    {
        var defs = BuiltIn();
        foreach (var u in ReadUser())
        {
            u.Origin = defs.RemoveAll(d => d.Id == u.Id) > 0 ? AppOrigin.Override : AppOrigin.Custom;
            defs.Add(u);
        }
        return defs;
    }

    /// <summary>Deletes a user entry: removes a custom app, or resets an overridden built-in.</summary>
    public static void RemoveUser(string id)
    {
        var all = ReadUser();
        if (all.RemoveAll(d => d.Id == id) == 0)
        {
            return;
        }

        File.WriteAllText(UserFile, JsonSerializer.Serialize(all, _opt));
    }

    public static void SaveUser(AppDefinition def)
    {
        var all = ReadUser();
        all.RemoveAll(d => d.Id == def.Id);
        all.Add(def);
        Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
        File.WriteAllText(UserFile, JsonSerializer.Serialize(all, _opt));
    }

    private static List<AppDefinition> ReadUser()
    {
        try { return File.Exists(UserFile) ? JsonSerializer.Deserialize<List<AppDefinition>>(File.ReadAllText(UserFile), _opt) ?? [] : []; }
        catch (Exception e) when (e is JsonException or IOException) { return []; }
    }
}
