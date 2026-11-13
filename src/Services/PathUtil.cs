using System.Diagnostics;
using System.IO;

namespace AiStackprint.Services;

public static class PathUtil
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Expands ~ and %ENV% and normalises separators.</summary>
    public static string Expand(string p)
    {
        p = Environment.ExpandEnvironmentVariables(p);
        if (p == "~" || p.StartsWith("~/") || p.StartsWith("~\\"))
            p = System.IO.Path.Combine(Home, p[1..].TrimStart('/', '\\'));
        return p.Replace('/', System.IO.Path.DirectorySeparatorChar);
    }

    public static bool OsMatches(string? os) => os?.ToLowerInvariant() switch
    {
        null or "" => true,
        "windows" => OperatingSystem.IsWindows(),
        "macos" => OperatingSystem.IsMacOS(),
        "linux" => OperatingSystem.IsLinux(),
        _ => false
    };

    /// <summary>Opens a file with its default app (a folder opens in the file manager).</summary>
    public static void OpenFile(string path)
    {
        if (Directory.Exists(path)) { OpenFolder(path); return; }
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", path);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    public static void OpenFolder(string path)
    {
        var dir = File.Exists(path) ? System.IO.Path.GetDirectoryName(path)! : path;
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"\"{dir}\"");
            else Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", dir);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
