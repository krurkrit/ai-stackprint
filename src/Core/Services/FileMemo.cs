// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// What was worked out from one file, kept by path and valid while the file's size and time are the same: in memory, and in <c>cache/&lt;name&gt;.json</c> so the
/// next start does not read the old log files again (only a file that changed, usually the newest, is read). A file that is missing, damaged or from another
/// format version is simply worked out again. <see cref="Flush"/> writes what is new; entries of files that no longer exist are dropped then.
/// </summary>
internal sealed class FileMemo<T> where T : class
{
    private const int Format = 1;
    private const int MaxEntries = 6000;

    private sealed record Entry(long Size, long Ticks, string Extra, T Value);

    private sealed record Stored(int Version, Dictionary<string, Entry> Files);

    private readonly string _name;
    private readonly Lock _lock = new();
    private ConcurrentDictionary<string, Entry>? _items;
    private string? _root;   // the data folder the entries were loaded from: tests and imports may point it elsewhere
    private volatile bool _dirty;

    public FileMemo(string name) => _name = name;

    private string FilePath => Path.Combine(DataFolder.Cache, _name + ".json");

    private ConcurrentDictionary<string, Entry> Items()
    {
        lock (_lock)
        {
            if (_items != null && _root == DataFolder.Root)
            {
                return _items;
            }

            _root = DataFolder.Root;
            _dirty = false;
            return _items = Load();
        }
    }

    private ConcurrentDictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Stored>(File.ReadAllBytes(FilePath)) is { Files: { } items } stored && stored.Version == Format)
            {
                return new ConcurrentDictionary<string, Entry>(items, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // a damaged or locked cache file: start empty, it is written again
        }

        return new(StringComparer.OrdinalIgnoreCase);
    }

    /// <param name="extra">Anything else the value depends on (for example the model table); a different value works it out again.</param>
    public T GetOrAdd(LogFile f, string extra, Func<T> make)
    {
        var items = Items();
        if (items.TryGetValue(f.Path, out var e) && e.Size == f.Size && e.Ticks == f.Modified.Ticks && e.Extra == extra)
        {
            return e.Value;
        }

        var value = make();
        items[f.Path] = new Entry(f.Size, f.Modified.Ticks, extra, value);
        _dirty = true;
        return value;
    }

    /// <summary>Writes the cache when something was added; drops entries of files that are gone.</summary>
    public void Flush()
    {
        if (!_dirty || _items == null)
        {
            return;
        }

        try
        {
            var items = _items;
            foreach (var key in items.Keys.Where(k => !File.Exists(k)).ToList())
            {
                items.TryRemove(key, out _);
            }

            if (items.Count > MaxEntries)
            {
                items.Clear();   // far more than any machine has files: start again rather than grow
            }

            Directory.CreateDirectory(DataFolder.Cache);
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(new Stored(Format, new Dictionary<string, Entry>(items))));
            File.Move(tmp, FilePath, true);
            _dirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the cache is only a speed-up: not written this time
        }
    }
}
