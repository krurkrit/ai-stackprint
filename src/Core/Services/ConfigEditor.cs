// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>Thrown when a file changed on disk after it was opened for editing.</summary>
public class FileChangedException(string path) : IOException($"{Path.GetFileName(path)} changed on disk after you opened it.");

/// <summary>Reads and saves app config files edited in the UI. Saving validates JSON, refuses to overwrite newer changes, and backs up the original first.</summary>
public static class ConfigEditor
{
    internal const long MaxEditBytes = 2 * 1024 * 1024;
    public static string BackupRoot => DataFolder.ConfigBackups;

    public record Stamp(DateTime Modified, long Length);

    public static Stamp StampOf(string path)
    {
        var f = new FileInfo(path);
        return new Stamp(f.LastWriteTimeUtc, f.Length);
    }

    public static bool CanEdit(string path) => File.Exists(path) && new FileInfo(path).Length <= MaxEditBytes;

    /// <summary>File text plus the stamp to pass back to <see cref="Save"/>.</summary>
    public static (string Text, Stamp Stamp) Read(string path)
    {
        var stamp = StampOf(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return (r.ReadToEnd(), stamp);
    }

    /// <summary>Error message when the text is not valid for its file type, else null. JSON is checked (comments and trailing commas allowed); other formats are not.</summary>
    public static string? Validate(string path, string text)
    {
        if (!Path.GetExtension(path).StartsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            return null;
        }
        catch (JsonException e) { return $"Invalid JSON: {e.Message}"; }
    }

    /// <summary>Validates, checks the file is unchanged since <paramref name="opened"/>, copies the original to BackupRoot/timestamp/, then writes. Returns the backup path.</summary>
    public static string Save(string path, string text, Stamp opened, string? backupRoot = null)
    {
        if (Validate(path, text) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        if (StampOf(path) != opened)
        {
            throw new FileChangedException(path);
        }

        var backup = Path.Combine(backupRoot ?? BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), Path.GetFileName(path));
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(path, backup);
        File.WriteAllText(path, text, new UTF8Encoding(HasBom(path)));   // keep the original's BOM choice
        return backup;
    }

    /// <summary>Writes a new file (and its folder); refuses when it exists. Returns nothing to back up.</summary>
    public static void Create(string path, string text)
    {
        if (File.Exists(path))
        {
            throw new IOException($"{Path.GetFileName(path)} already exists.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);   // CreateNew: never overwrites, even if it appeared a moment ago
        using var w = new StreamWriter(fs, new UTF8Encoding(false));
        w.Write(text);
    }

    private static bool HasBom(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> b = stackalloc byte[3];
        return fs.Read(b) == 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
    }
}
