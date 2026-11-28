// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.Versioning;
using System.Security;

using AiStackprint.Services;

namespace AiStackprint.Ui;

internal static partial class Notifier
{
    /// <summary>
    /// A Windows toast through WinRT's <c>ToastNotificationManager</c>, called with COM function pointers (no Windows SDK projection, the app stays <c>net10.0</c>).
    /// An unpackaged app needs its app id under <c>HKCU\Software\Classes\AppUserModelId</c> (name and icon the toast shows) and the <c>aistackprint:</c>
    /// link under <c>HKCU\Software\Classes</c> for its buttons; both are written only while alerts can be sent and removed when they are turned off or the app is uninstalled.
    /// </summary>
    internal sealed partial class WindowsToast(INotifier fallback) : INotifier
    {
        public const string AppId = "AiStackprint.App";
        private const string Classes = @"Software\Classes\";
        private bool _broken;

        public void Register(bool on)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                if (on)
                {
                    Write(Environment.ProcessPath ?? "");
                }
                else
                {
                    Remove();
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { _broken = true; }   // pop-ups instead
        }

        [SupportedOSPlatform("windows")]
        private static void Write(string exe)
        {
            var icon = Path.Combine(DataFolder.Cache, "app-icon.png");
            if (!File.Exists(icon))
            {
                Directory.CreateDirectory(DataFolder.Cache);
                using var app = new Avalonia.Media.Imaging.Bitmap(typeof(Notifier).Assembly.GetManifestResourceStream("app-icon.ico")!);
                app.Save(icon, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }

            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Classes + @"AppUserModelId\" + AppId))
            {
                k.SetValue("DisplayName", "AI Stackprint");
                k.SetValue("IconUri", icon);
            }

            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Classes + SingleInstance.Protocol))
            {
                k.SetValue("", "URL:AI Stackprint");
                k.SetValue("URL Protocol", "");
                using var cmd = k.CreateSubKey(@"shell\open\command");
                cmd.SetValue("", $"\"{exe}\" \"%1\"");
            }
        }

        /// <summary>Removes the app id and the link (switch off, and the uninstall hook in Program).</summary>
        public static void Remove()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Classes + @"AppUserModelId\" + AppId, false);
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Classes + SingleInstance.Protocol, false);
        }

        public void Show(Alert a)
        {
            if (_broken || !OperatingSystem.IsWindows() || !Toast(Xml(a)))
            {
                _broken = true;
                fallback.Show(a);
            }
        }

        /// <summary>The toast: title, text, and its buttons as <c>aistackprint:</c> links; clicking the toast itself opens the app's page.</summary>
        public static string Xml(Alert a)
        {
            static string E(string s) => SecurityElement.Escape(s);
            var open = E(OpenLink(a.AppId));
            var actions = (a.Open ? $"<action content=\"Open\" activationType=\"protocol\" arguments=\"{open}\"/>" : "")
                + (a.Pause ? $"<action content=\"Pause alerts for 1 hour\" activationType=\"protocol\" arguments=\"{E(PauseLink)}\"/>" : "");
            return $"<toast activationType=\"protocol\" launch=\"{open}\"><visual><binding template=\"ToastGeneric\"><text>{E(a.Title)}</text><text>{E(a.Text)}</text></binding></visual>"
                + (actions.Length > 0 ? $"<actions>{actions}</actions>" : "") + "</toast>";
        }
    }
}
