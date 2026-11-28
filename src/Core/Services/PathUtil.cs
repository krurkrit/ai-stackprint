// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace AiStackprint.Services;

public static class PathUtil
{
    private static string? _home;

    public static string Home => _home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Another home folder, for tests (null = the real one): the AI tools' folders (~/.claude, …) and the data folder (~/.ai-stackprint) all follow it, so a whole window can run on made-up files.</summary>
    internal static void UseHome(string? home) => _home = home;

    /// <summary>A folder of the OS such as (local) app data; with a test home (<see cref="UseHome"/>) a folder inside it, so a test never reads or moves files in the real one.</summary>
    public static string SystemFolder(Environment.SpecialFolder folder) => _home != null ? Path.Combine(_home, "system", folder.ToString()) : Environment.GetFolderPath(folder);

    /// <summary>Expands ~ and %ENV% and normalises separators.</summary>
    public static string Expand(string p)
    {
        p = Environment.ExpandEnvironmentVariables(p);
        if (p == "~" || p.StartsWith("~/") || p.StartsWith("~\\"))
        {
            p = Path.Combine(Home, p[1..].TrimStart('/', '\\'));
        }

        return p.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// What a folder listing skips: hidden and system entries on Windows, system ones only on Linux and macOS. There .NET counts every
    /// name that starts with a dot as hidden, and those are the AI files (.claude-plugin, .mcp.json, .cursorignore, .github).
    /// </summary>
    public static readonly FileAttributes Skipped = OperatingSystem.IsWindows() ? FileAttributes.Hidden | FileAttributes.System : FileAttributes.System;

    private static readonly AsyncLocal<string?> _osOverride = new();

    /// <summary>The operating system as the app definitions name it: windows, macos or linux. Tests set another to run the other branches.</summary>
    internal static string? OsOverride { get => _osOverride.Value; set => _osOverride.Value = value; }   // per test flow: tests running in parallel do not see each other's

    private static string Os => OsOverride ?? (OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux");

    public static bool OsMatches(string? os) => string.IsNullOrEmpty(os) || string.Equals(os, Os, StringComparison.OrdinalIgnoreCase);

    /// <summary>Starts a program (the file manager, a file's default app). Tests replace it so nothing opens.</summary>
    internal static Action<ProcessStartInfo> Launcher { get; set; } = p => Process.Start(p);

    /// <summary>Opens a file with its default app (a folder opens in the file manager).</summary>
    public static void OpenFile(string path)
    {
        if (Directory.Exists(path))
        {
            OpenFolder(path);
            return;
        }
        Start(Os == "windows" ? new ProcessStartInfo(path) { UseShellExecute = true } : Tool(path));
    }

    public static void OpenFolder(string path)
    {
        var dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        Start(Os == "windows" ? new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{dir}\"") : Tool(dir));
    }

    private static ProcessStartInfo Tool(string argument) => new(Os == "macos" ? "open" : "xdg-open") { ArgumentList = { argument } };

    private static void Start(ProcessStartInfo info)
    {
        try { Launcher(info); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }   // no program for it: nothing opens
    }
}
