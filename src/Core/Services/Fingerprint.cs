// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// SHA-256 fingerprints of what a skill, plugin or MCP server is made of, to tell when it changed between two scans. Only the hash is kept (in last-scan.json),
/// never the content. Folders: at most 300 files, hidden folders skipped (<see cref="SkillDoc.Files"/>). Files up to 256 KB are hashed by content, up to 4 MB per folder; larger files count by size only. A file is hashed again only when its size or time changed.
/// </summary>
public static class Fingerprint
{
    private const long MaxBytes = 4L * 1024 * 1024, MaxHashed = 256 * 1024;

    public static string Text(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>The fingerprint of a skill or an agent plugin (null when it has nothing readable to fingerprint, or is an editor extension).</summary>
    public static string? Of(Item i) => SafetyScan.Target(i) is { Root: { } root } t ? Folder(root, t.Single) : null;

    public static string? Folder(string root, bool single)
    {
        try
        {
            var dir = single ? Path.GetDirectoryName(root)! : root;
            var files = single ? [(Path.GetFileName(root), new FileInfo(root).Length)] : SkillDoc.Files(root);
            if (files.Count == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            long total = 0;
            foreach (var (rel, size) in files.OrderBy(f => f.Rel, StringComparer.Ordinal))
            {
                total += size;

                var full = Path.Combine(dir, rel);
                var h = size > MaxHashed || total > MaxBytes ? size.ToString() : ScanCache.Hashes.GetOrAdd(ScanCache.Stamp(full, size), _ => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))));
                sb.Append(rel.Replace('\\', '/')).Append(':').Append(h).Append('\n');
            }

            return Text(sb.ToString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
