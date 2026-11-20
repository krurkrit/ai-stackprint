// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiStackprint.Services;

/// <summary>A project folder in the list, in the order it was added.</summary>
/// <param name="Path">Full path on this machine.</param>
/// <param name="Added">When it was added (UTC); kept when the folder is relocated.</param>
public sealed record SavedProject(string Path, DateTime Added);

/// <summary>
/// projects.json in the data folder: the project folders you added, so they are back on the next start. Paths under your home folder are
/// written as <c>~/…</c> (with <c>/</c>), so the file still works for another user or after the home folder moves. Format: <see cref="VersionedJson"/>.
/// </summary>
public static class ProjectStore
{
    internal const string FileName = "projects.json";
    private static readonly VersionedJson _format = new("projects", 1);

    public static string File => Path.Combine(DataFolder.Root, FileName);

    /// <summary>Shown once when projects.json comes from a newer AI Stackprint: changes to the list are not saved.</summary>
    public static string? Problem => _format.IsNewer(File)
        ? "projects.json comes from a newer AI Stackprint: project folders you add or remove here are not saved."
        : null;

    /// <summary>The saved list, without duplicates or empty paths; empty when there is no file or it cannot be read.</summary>
    public static List<SavedProject> Load(string? file = null, string? home = null)
    {
        file ??= File;
        home ??= PathUtil.Home;
        JsonObject? data;
        try { data = _format.Read(file, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "backups")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }

        var list = new List<SavedProject>();
        if (data?["projects"] is not JsonArray a)
        {
            return list;
        }

        foreach (var n in a)
        {
            if (n is not JsonObject o || o["path"] is not JsonValue pv || !pv.TryGetValue(out string? p) || string.IsNullOrWhiteSpace(p))
            {
                continue;   // one bad entry does not lose the others
            }

            var path = FromFile(p, home);
            var added = o["added"] is JsonValue av && av.TryGetValue(out DateTime d) ? d.ToUniversalTime() : DateTime.UnixEpoch;
            if (!list.Exists(x => Same(x.Path, path)))
            {
                list.Add(new(path, added));
            }
        }
        return list;
    }

    /// <summary>Writes the list (duplicates dropped); an error leaves the list remembered for this run only.</summary>
    public static void Save(IEnumerable<SavedProject> projects, string? file = null, string? home = null)
    {
        home ??= PathUtil.Home;
        var a = new JsonArray();
        var seen = new List<string>();
        foreach (var p in projects)
        {
            if (!seen.Exists(s => Same(s, p.Path)))
            {
                seen.Add(p.Path);
                a.Add(new JsonObject
                {
                    ["path"] = ToFile(p.Path, home),
                    ["added"] = p.Added.ToUniversalTime()
                });
            }
        }

        try { _format.Write(file ?? File, new JsonObject { ["projects"] = a }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    /// <summary>Two paths name the same folder (case is ignored on Windows and macOS; a trailing separator is ignored).</summary>
    public static bool Same(string a, string b) => string.Equals(Trim(a), Trim(b), Comparison);

    /// <summary><paramref name="path"/> as written in the file: <c>~/a/b</c> when it is under <paramref name="home"/>, else as it is.</summary>
    internal static string ToFile(string path, string home)
    {
        var h = Trim(home);
        var p = Trim(path);
        if (h.Length == 0 || !p.StartsWith(h, Comparison))
        {
            return p;
        }

        if (p.Length == h.Length)
        {
            return "~";
        }

        return p[h.Length] is '/' or '\\' ? "~/" + p[(h.Length + 1)..].Replace('\\', '/') : p;   // /home/ab is not under /home/a
    }

    /// <summary>A path from the file on this machine: <c>~/a/b</c> becomes home + a + b with this system's separator.</summary>
    internal static string FromFile(string path, string home, char separator = '\0')
    {
        var sep = separator == '\0' ? Path.DirectorySeparatorChar : separator;
        if (path == "~")
        {
            return Trim(home);
        }

        if (!path.StartsWith("~/", StringComparison.Ordinal) && !path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return path;
        }

        return Trim(home) + sep + path[2..].Replace('/', sep).Replace('\\', sep);
    }

    private static StringComparison Comparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Without a trailing separator, except a root such as <c>/</c> or <c>C:\</c>.</summary>
    private static string Trim(string p)
    {
        var t = p.TrimEnd('/', '\\');
        return t.Length == 0 || t.EndsWith(':') ? p : t;
    }
}
