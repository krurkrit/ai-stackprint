// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia;

using Velopack;

namespace AiStackprint;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // First: the installer starts the app with hook arguments and expects it to exit at once. Uninstalling removes the login entry and the notification link.
        var setup = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
        {
            setup = setup.OnBeforeUninstallFastCallback(_ => Uninstall());
        }

        setup.Run();
        Services.DataFolder.Prepare();   // moves an older version's files to ~/.ai-stackprint before anything reads or writes them
        if (!args.Contains("--perf") && !SingleInstance.Claim(SingleInstance.Message(args)))
        {
            return 0;   // a copy is running: it opens its window (or does what a notification's button asked)
        }

        Services.CrashLog.Install();   // a crash leaves its stack trace in crash.log
        return AppBuilder.Configure<AiStackprintApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }

    private static void Uninstall()
    {
        try
        {
            LoginItem.Current.Set(false, "");
            Notifier.WindowsToast.Remove();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }   // nothing to undo, or not allowed: the uninstall goes on
    }
}
