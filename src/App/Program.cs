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
        VelopackApp.Build().Run();   // first: the installer starts the app with hook arguments and expects it to exit at once
        Services.CrashLog.Install();   // a crash leaves its stack trace in crash.log
        return AppBuilder.Configure<AiStackprintApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
