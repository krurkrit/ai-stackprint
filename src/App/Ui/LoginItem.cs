// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security;

namespace AiStackprint.Ui;

/// <summary>Starts the app in the tray at login. Tests replace <see cref="LoginItem.Current"/> with a fake, so they never touch the registry or the user's folders.</summary>
internal interface ILoginItem
{
    bool IsOn { get; }
    /// <summary>Writes the entry (<paramref name="exe"/> <c>--tray</c>) or removes it.</summary>
    void Set(bool on, string exe);
}

/// <summary>
/// The platform's login entry, written only while Settings › Usage limits › Start at login is on and removed when it is turned off or the app is uninstalled:
/// Windows <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, macOS <c>~/Library/LaunchAgents/</c>, Linux <c>~/.config/autostart/</c>.
/// The path is the running exe: Velopack keeps it in the same <c>current</c> folder across updates.
/// </summary>
internal static class LoginItem
{
    public const string Name = "AI Stackprint", Id = "io.github.aistackprint";

    public static ILoginItem Current { get; set; } = OperatingSystem.IsWindows() ? new WindowsRun() : OperatingSystem.IsMacOS() ? new FileEntry(true) : new FileEntry(false);

    /// <summary>Makes the entry match the setting (on start and when it changes); a failure to write is reported, not thrown.</summary>
    public static string? Apply(bool on)
    {
        try
        {
            if (on || Current.IsOn)   // on: written again, in case the exe moved
            {
                Current.Set(on, Environment.ProcessPath ?? "");
            }

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return "The login entry could not be changed: " + e.Message;
        }
    }

    private sealed class WindowsRun : ILoginItem
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public bool IsOn => OperatingSystem.IsWindows() && Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)?.GetValue(Name) != null;

        public void Set(bool on, string exe)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
            if (on)
            {
                k.SetValue(Name, $"\"{exe}\" --tray");
            }
            else
            {
                k.DeleteValue(Name, false);
            }
        }
    }

    /// <summary>A LaunchAgent plist (macOS) or an autostart .desktop file (Linux).</summary>
    private sealed class FileEntry(bool mac) : ILoginItem
    {
        private string File => mac
            ? Path.Combine(Services.PathUtil.Home, "Library", "LaunchAgents", Id + ".plist")
            : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Services.PathUtil.Home, ".config"), "autostart", "ai-stackprint.desktop");

        public bool IsOn => System.IO.File.Exists(File);

        public void Set(bool on, string exe)
        {
            if (!on)
            {
                System.IO.File.Delete(File);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, mac ? Plist(exe) : Desktop(exe));
        }

        private static string Plist(string exe) => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{Id}</string>
              <key>ProgramArguments</key><array><string>{SecurityElement.Escape(exe)}</string><string>--tray</string></array>
              <key>RunAtLoad</key><true/>
            </dict>
            </plist>

            """;

        private static string Desktop(string exe) => $"""
            [Desktop Entry]
            Type=Application
            Name={Name}
            Exec="{exe.Replace("\"", "\\\"")}" --tray
            X-GNOME-Autostart-enabled=true

            """;
    }
}
