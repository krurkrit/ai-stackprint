// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// The only code that changes skill folders, and only on an explicit user action.
/// Install copies a skill folder into an app's skills root. Remove moves the folder to a backup, never deletes it.
/// </summary>
public static class SkillManager
{
    public static string BackupRoot => Path.Combine(AppCatalog.Folder, "removed");

    /// <summary>Folder that receives new skills for an app at a level, or null when the app has no folder-style skills root there.</summary>
    public static string? TargetRoot(AppDefinition d, string level, string? project)
    {
        if (level == "Project")
        {
            return project == null ? null : d.ProjectRoots.Where(Fits)
                .Select(r => Path.Combine(project, r.Path.Replace('/', Path.DirectorySeparatorChar))).FirstOrDefault();
        }

        return level == "User" ? d.Roots.Where(r => r.Level == "User" && Fits(r)).Select(r => PathUtil.Expand(r.Path)).FirstOrDefault() : null;

        static bool Fits(ScanRoot r) => r.Layout == "skill-folders" && !r.Bundled && PathUtil.OsMatches(r.Os);
    }

    /// <summary>Copies <paramref name="source"/> (a folder containing SKILL.md) to targetRoot/name. Returns the new folder.</summary>
    public static string Install(string source, string targetRoot)
    {
        if (!File.Exists(Path.Combine(source, "SKILL.md")))
        {
            throw new InvalidOperationException("The folder has no SKILL.md.");
        }

        var dest = Path.Combine(targetRoot, Path.GetFileName(source.TrimEnd('/', '\\')));
        if (Directory.Exists(dest) || File.Exists(dest))
        {
            throw new InvalidOperationException($"A skill named \"{Path.GetFileName(dest)}\" already exists there.");
        }

        CopyDir(source, dest);
        return dest;
    }

    /// <summary>Why a skill cannot be removed here, or null when it can.</summary>
    public static string? CannotRemove(Item skill) =>
        skill.Kind != "Skill" ? "Not a skill." :
        skill.Plugin != null ? "This skill ships inside a plugin. Remove the plugin instead." :
        skill.Bundled ? "This skill ships with the app itself." :
        skill.Level == "System" ? "System-level skills are managed by an administrator." : null;

    /// <summary>Moves the skill (its folder, or the .md file for flat layouts) to BackupRoot/timestamp/. Returns the backup path.</summary>
    public static string Remove(Item skill, string? backupRoot = null)
    {
        if (CannotRemove(skill) is { } why)
        {
            throw new InvalidOperationException(why);
        }

        var folder = Path.GetFileName(skill.Path).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase);
        var src = folder ? Path.GetDirectoryName(skill.Path)! : skill.Path;
        var dest = Path.Combine(backupRoot ?? BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), Path.GetFileName(src));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (folder)
        {
            MoveDir(src, dest);
        }
        else
        {
            File.Move(src, dest);
        }

        return dest;
    }

    private static void MoveDir(string src, string dest)
    {
        try { Directory.Move(src, dest); }
        catch (IOException) when (Path.GetPathRoot(src) != Path.GetPathRoot(dest))
        {
            CopyDir(src, dest);   // different volume: copy, then delete the original
            Directory.Delete(src, true);
        }
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.EnumerateFiles(src))
        {
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        }

        foreach (var d in Directory.EnumerateDirectories(src))
        {
            if (new DirectoryInfo(d).LinkTarget == null)
            {
                CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));   // skip symlinked folders
            }
        }
    }
}
