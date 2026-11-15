// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiStackprint.Services;

/// <summary>
/// One of the app's own JSON files with a format version (<c>"version"</c>; missing = 1). A file of an older version is upgraded one step at a
/// time (v1→v2→v3) after a copy to the backups folder, and written back. A file of a newer version is read for what this app knows and never
/// written (<see cref="IsNewer"/>), so going back to an older app loses nothing. Writes go to a temporary file first, then replace the old one.
/// Comments and trailing commas are allowed when reading; a file that is not JSON is copied to backups before it would be replaced.
/// </summary>
/// <param name="name">File name for backups (<c>settings</c> → <c>settings.v1.json</c>).</param>
/// <param name="current">The version this app writes.</param>
/// <param name="upgrades">Step <c>i</c> turns version <c>i + 1</c> into <c>i + 2</c>; <c>current - 1</c> steps.</param>
public sealed class VersionedJson(string name, int current, params Func<JsonObject, JsonObject>[] upgrades)
{
    public const string Key = "version";
    private static readonly JsonDocumentOptions _read = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions _write = new() { WriteIndented = true };
    private readonly HashSet<string> _newer = new(StringComparer.OrdinalIgnoreCase);

    public int Current => current;

    /// <summary>A file that is a bare JSON list is version 1 of a file whose data is that list under this key (apps.json before it had a version).</summary>
    public string? ListKey { get; init; }

    /// <summary>The file at this path had a newer version than this app: it is not written.</summary>
    public bool IsNewer(string path) => _newer.Contains(Path.GetFullPath(path));

    /// <summary>The file's data at <see cref="Current"/> (without <see cref="Key"/>), or null when there is no file or it is not a JSON object.</summary>
    public JsonObject? Read(string path, string backups)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        JsonObject data;
        try
        {
            switch (JsonNode.Parse(File.ReadAllText(path), documentOptions: _read))
            {
                case JsonObject o:
                    data = o;
                    break;
                case JsonArray a when ListKey != null:
                    data = new JsonObject { [ListKey] = a };   // no version: 1
                    break;
                default:
                    Keep(path, backups, "broken");
                    return null;
            }
        }
        catch (JsonException)
        {
            Keep(path, backups, "broken");   // a later save would replace it: keep what was there
            return null;
        }

        var version = data[Key] is JsonValue v && v.TryGetValue(out int n) ? n : 1;
        data.Remove(Key);
        if (version > current)
        {
            _newer.Add(Path.GetFullPath(path));
            return data;
        }

        if (version < current)
        {
            Keep(path, backups, "v" + version);
            for (var step = Math.Max(version, 1); step < current; step++)
            {
                data = upgrades[step - 1](data);
            }

            Write(path, data);
        }
        return data;
    }

    /// <summary>Writes <paramref name="data"/> with <see cref="Key"/> first; nothing when the file on disk is newer than this app.</summary>
    public void Write(string path, JsonObject data)
    {
        if (IsNewer(path))
        {
            return;
        }

        var file = new JsonObject { [Key] = current };
        foreach (var (k, v) in data)
        {
            if (k != Key)
            {
                file[k] = v?.DeepClone();
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, file.ToJsonString(_write) + "\n");
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Copies the file to <c>backups/name.tag.json</c> (a time stamp is added when that name is taken).</summary>
    private void Keep(string path, string backups, string tag)
    {
        try
        {
            Directory.CreateDirectory(backups);
            var dest = Path.Combine(backups, $"{name}.{tag}.json");
            if (File.Exists(dest))
            {
                dest = Path.Combine(backups, $"{name}.{tag}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            }

            File.Copy(path, dest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // reading goes on; the original stays until a save
    }
}
