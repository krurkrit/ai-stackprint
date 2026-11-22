// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>What <see cref="AppCatalog.Read(string)"/> found.</summary>
/// <param name="Defs">Built-in apps, then your custom ones.</param>
/// <param name="Shadowed">Your apps for a tool that is now built in: not used (the built-in wins), offered for removal.</param>
public sealed record Catalog(List<AppDefinition> Defs, List<AppDefinition> Shadowed);

/// <summary>
/// The built-in apps (default/apps.json), then your custom apps from apps.json in the data folder. A custom app never replaces a built-in one:
/// its id starts with <see cref="CustomPrefix"/>, Add app refuses a built-in id or name, and when a later version supports the same tool the built-in wins.
/// Format: <see cref="VersionedJson"/>, version 1 (before 1.0 the structure may change without a version bump).
/// </summary>
public static class AppCatalog
{
    public static readonly string CustomPrefix = "custom.";

    internal static readonly JsonSerializerOptions _opt = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly VersionedJson _format = new("apps", 1) { ListKey = "apps" };

    public static string UserFile => Path.Combine(DataFolder.Root, "apps.json");

    /// <summary>Shown once when apps.json comes from a newer AI Stackprint: apps you add or remove here are not saved.</summary>
    public static string? Problem => _format.IsNewer(UserFile)
        ? "apps.json comes from a newer AI Stackprint: apps you add or remove here are not saved."
        : null;

    /// <summary>The built-in app definitions only (default/apps.json), a new list on each call.</summary>
    public static List<AppDefinition> BuiltIn()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("default/apps.json")!;
        var o = JsonNode.Parse(s, documentOptions: new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        })!.AsObject();
        return o["apps"].Deserialize<List<AppDefinition>>(_opt) ?? [];
    }

    public static List<AppDefinition> Load(string? file) => Read(file).Defs;

    public static List<AppDefinition> Load() => Load(null);

    public static Catalog Read(string? file)
    {
        var builtIn = BuiltIn();
        var defs = new List<AppDefinition>(builtIn);
        var shadowed = new List<AppDefinition>();
        foreach (var u in ReadUser(file ?? UserFile))
        {
            u.Origin = AppOrigin.Custom;
            if (BuiltInFor(u, builtIn) != null)
            {
                shadowed.Add(u);
            }
            else if (!defs.Exists(d => d.Id == u.Id))
            {
                defs.Add(u);
            }
        }
        return new(defs, shadowed);
    }

    public static Catalog Read() => Read(null);

    /// <summary>Why <paramref name="def"/> cannot be added (a built-in app has its id or name), or null.</summary>
    public static string? Refuse(AppDefinition def) =>
        BuiltInFor(def, BuiltIn()) is { } b ? $"{b.Name} is already supported: it is built in. Use its page, or Settings › Apps to show it again if it is hidden." : null;

    /// <summary>Adds or replaces a custom app (the id gets <see cref="CustomPrefix"/>); returns why it was refused, or null.</summary>
    public static string? SaveUser(AppDefinition def, string? file)
    {
        if (!def.Id.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            def.Id = CustomPrefix + def.Id;
        }

        if (Refuse(def) is { } why)
        {
            return why;
        }

        file ??= UserFile;
        var all = ReadUser(file);
        all.RemoveAll(d => d.Id == def.Id);
        all.Add(def);
        Write(all, file);
        return null;
    }

    public static string? SaveUser(AppDefinition def) => SaveUser(def, null);

    /// <summary>Deletes your entry with this id from apps.json (a custom app, or one a built-in app replaced).</summary>
    public static void RemoveUser(string id, string? file)
    {
        file ??= UserFile;
        var all = ReadUser(file);
        if (all.RemoveAll(d => d.Id == id) > 0)
        {
            Write(all, file);
        }
    }

    public static void RemoveUser(string id) => RemoveUser(id, null);

    /// <summary>The built-in app with the same id (with or without <see cref="CustomPrefix"/>) or the same name.</summary>
    private static AppDefinition? BuiltInFor(AppDefinition def, List<AppDefinition> builtIn)
    {
        var bare = def.Id.StartsWith(CustomPrefix, StringComparison.Ordinal) ? def.Id[CustomPrefix.Length..] : def.Id;
        return builtIn.Find(b => b.Id == def.Id || b.Id == bare || string.Equals(b.Name.Trim(), def.Name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static void Write(List<AppDefinition> all, string file) =>
        _format.Write(file, new JsonObject { ["apps"] = JsonSerializer.SerializeToNode(all, _opt) });

    private static List<AppDefinition> ReadUser(string file)
    {
        try
        {
            var data = _format.Read(file, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "backups"));
            return data?["apps"]?.Deserialize<List<AppDefinition>>(_opt) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }
}
