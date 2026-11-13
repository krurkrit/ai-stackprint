// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Services;

/// <summary>
/// Watches the folders one app's logs live in. Events are batched: after <see cref="Quiet"/> without events, <see cref="Changed"/>
/// gets the files that changed and whether files were added, removed or renamed (the list must be found again).
/// A stat of the newest file every few seconds backs it up (FileSystemWatcher can miss events on network or synced drives).
/// Read-only: it never opens a file. Dispose when the app is deselected or the page closes.
/// </summary>
public sealed class LogFolderWatcher : IDisposable
{
    /// <param name="Structure">A file or folder was created, deleted or renamed.</param>
    public record Batch(IReadOnlyCollection<string> Paths, bool Structure);

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _debounce, _poll;
    private readonly object _lock = new();
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _newest;
    private (long Size, long Ticks) _stamp;
    private bool _structure, _pending;

    public event Action<Batch>? Changed;
    public TimeSpan Quiet { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <param name="roots">Folder, file pattern and whether sub-folders count (see <see cref="LogReader.Roots"/>).</param>
    /// <param name="newest">The file being written; polled as a backup.</param>
    public LogFolderWatcher(IEnumerable<(string Dir, string Pattern, bool Recursive)> roots, string? newest, TimeSpan? poll = null)
    {
        _newest = newest;
        _stamp = Stamp();
        _debounce = new Timer(_ => Flush());
        foreach (var (dir, pattern, recursive) in roots.Distinct())
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            try
            {
                var w = new FileSystemWatcher(dir, pattern) { IncludeSubdirectories = recursive, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                w.Changed += (_, e) => Kick(e.FullPath, false);
                w.Created += (_, e) => Kick(e.FullPath, true);
                w.Deleted += (_, e) => Kick(e.FullPath, true);
                w.Renamed += (_, e) => { Kick(e.OldFullPath, true); Kick(e.FullPath, true); };
                w.Error += (_, _) => Kick(dir, true);   // buffer overflow: find the files again
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException) { }   // the poll still covers the newest file
        }
        var every = poll ?? TimeSpan.FromSeconds(10);
        _poll = new Timer(_ => Poll(), null, every, every);
    }

    private void Kick(string path, bool structure)
    {
        lock (_lock)
        {
            _paths.Add(path);
            _structure |= structure;
            if (_pending)
            {
                return;
            }

            _pending = true;
        }
        try { _debounce.Change(Quiet, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }   // stopped while an event was in flight
    }

    private void Flush()
    {
        Batch b;
        lock (_lock)
        {
            b = new Batch(_paths.ToList(), _structure);
            _paths.Clear(); _structure = false; _pending = false;
        }
        Changed?.Invoke(b);
    }

    private void Poll()
    {
        if (_newest == null)
        {
            return;
        }

        var now = Stamp();
        if (now != _stamp) { _stamp = now; Kick(_newest, now.Size < 0); }
    }

    private (long Size, long Ticks) Stamp()
    {
        if (_newest == null)
        {
            return default;
        }

        try { var i = new FileInfo(_newest); return i.Exists ? (i.Length, i.LastWriteTimeUtc.Ticks) : (-1, 0); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (-2, 0); }
    }

    public void Dispose()
    {
        _poll.Dispose(); _debounce.Dispose();
        foreach (var w in _watchers)
        {
            w.Dispose();
        }

        Changed = null;
    }
}
