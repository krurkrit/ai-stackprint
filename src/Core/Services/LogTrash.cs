using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Deletes log files by moving them to the Recycle Bin (Windows) or Trash (macOS, Linux), so they can be restored.
/// Only files the app itself listed as logs can be deleted.
/// </summary>
public static class LogTrash
{
    /// <summary>Files changed within this time are probably still being written (an open session).</summary>
    public static readonly TimeSpan Active = TimeSpan.FromMinutes(1);

    public static bool IsActive(LogFile f, DateTime? now = null) => (now ?? DateTime.Now) - f.Modified < Active;

    /// <summary>Files older than <paramref name="days"/> days.</summary>
    public static List<LogFile> OlderThan(IEnumerable<LogFile> files, int days, DateTime? now = null) =>
        files.Where(f => (now ?? DateTime.Now) - f.Modified > TimeSpan.FromDays(days)).ToList();

    /// <summary>Moves each file to the bin. Refuses paths not in <paramref name="allowed"/>. Returns (moved, failed with reason).</summary>
    public static (List<string> Moved, List<(string Path, string Reason)> Failed) Delete(IEnumerable<string> paths, IEnumerable<LogFile> allowed, string? trashRoot = null)
    {
        var ok = allowed.Select(f => Path.GetFullPath(f.Path)).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var moved = new List<string>(); var failed = new List<(string, string)>();
        foreach (var p in paths)
        {
            var full = Path.GetFullPath(p);
            if (!ok.Contains(full)) { failed.Add((p, "Not one of this app's log files.")); continue; }
            try { ToTrash(full, trashRoot); moved.Add(p); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { failed.Add((p, e.Message)); }
        }
        return (moved, failed);
    }

    static void ToTrash(string path, string? trashRoot)
    {
        if (trashRoot == null && OperatingSystem.IsWindows())
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            return;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (trashRoot == null && OperatingSystem.IsMacOS())
        {
            File.Move(path, Unique(Path.Combine(home, ".Trash"), Path.GetFileName(path)));
            return;
        }
        // freedesktop.org Trash spec: files/ holds the file, info/<name>.trashinfo records where it came from
        var root = trashRoot ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local", "share"), "Trash");
        Directory.CreateDirectory(Path.Combine(root, "files"));
        Directory.CreateDirectory(Path.Combine(root, "info"));
        var dest = Unique(Path.Combine(root, "files"), Path.GetFileName(path));
        File.WriteAllText(Path.Combine(root, "info", Path.GetFileName(dest) + ".trashinfo"),
            $"[Trash Info]\nPath={Uri.EscapeDataString(path).Replace("%2F", "/")}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
        File.Move(path, dest);
    }

    /// <summary>folder/name, or folder/name.2, .3 … when taken.</summary>
    static string Unique(string folder, string name)
    {
        var p = Path.Combine(folder, name);
        for (int i = 2; File.Exists(p) || Directory.Exists(p); i++) p = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}.{i}{Path.GetExtension(name)}");
        return p;
    }
}
