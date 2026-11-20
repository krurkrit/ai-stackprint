// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>A file as it was when read: a result stays valid while the path, size and last-write time are the same.</summary>
public readonly record struct FileStamp(string Path, long Size, long Ticks);

/// <summary>
/// Keeps, between runs, what the safety scan and the change fingerprints found per file, so a start reads only files that changed since the last run.
/// Saved to scan-cache.json: path, size, time, the ids of the safety rules that matched and the SHA-256 of the content. Never any content.
/// A missing or unreadable cache only means the files are read again.
/// </summary>
public static class ScanCache
{
    internal static readonly ConcurrentDictionary<FileStamp, string[]> Rules = new();
    internal static readonly ConcurrentDictionary<FileStamp, string> Hashes = new();

    private static readonly Lock _gate = new();
    private static bool _loaded;
    private static volatile bool _dirty;   // a file was read this run: the saved cache is out of date

    /// <summary>The rule ids for this stamp, matched by <paramref name="match"/> only when the file is new or changed.</summary>
    internal static string[] Rule(FileStamp key, Func<FileStamp, string[]> match) => Rules.GetOrAdd(key, k =>
    {
        _dirty = true;
        return match(k);
    });

    /// <summary>The SHA-256 for this stamp, computed by <paramref name="hash"/> only when the file is new or changed.</summary>
    internal static string Hash(FileStamp key, Func<FileStamp, string> hash) => Hashes.GetOrAdd(key, k =>
    {
        _dirty = true;
        return hash(k);
    });

    public static string File => Path.Combine(DataFolder.Cache, "scan-cache.json");

    private sealed record Entry(string P, long S, long T, string[]? R, string? H);

    /// <summary>Reads the saved cache once per run; later calls do nothing.</summary>
    public static void Load(string? file = null)
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            try
            {
                var entries = JsonSerializer.Deserialize<List<Entry>>(System.IO.File.ReadAllText(file ?? File)) ?? [];
                foreach (var e in entries.Where(e => e.P != null))
                {
                    var key = new FileStamp(e.P, e.S, e.T);
                    if (e.R != null)
                    {
                        Rules.TryAdd(key, e.R);
                    }

                    if (e.H != null)
                    {
                        Hashes.TryAdd(key, e.H);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }   // none yet, or unreadable: read the files again
        }
    }

    /// <summary>Writes the cache: per path only its newest stamp, and only files that still exist. Nothing when no file was read since the last save.</summary>
    public static void Save(string? file = null)
    {
        if (!_dirty)
        {
            return;   // every file was as last time: the saved cache is already right
        }

        _dirty = false;
        var keys = Rules.Keys.Concat(Hashes.Keys).GroupBy(k => k.Path, StringComparer.Ordinal).Select(g => g.MaxBy(k => k.Ticks)).Where(k => System.IO.File.Exists(k.Path));
        var entries = keys.Select(k => new Entry(k.Path, k.Size, k.Ticks, Rules.GetValueOrDefault(k), Hashes.GetValueOrDefault(k))).ToList();
        var path = file ?? File;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(entries));
            System.IO.File.Move(tmp, path, true);   // a run that stops mid-write leaves the old cache, not half a file
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // only a slower next start
    }

    /// <summary>Forgets everything (tests).</summary>
    internal static void Clear()
    {
        lock (_gate)
        {
            Rules.Clear();
            Hashes.Clear();
            _loaded = false;
            _dirty = false;
        }
    }

    internal static FileStamp Stamp(string file, long size) => new(file, size, System.IO.File.GetLastWriteTimeUtc(file).Ticks);
}
