using AiStackprint.Ui;

using Avalonia;

namespace AiStackprint;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Services.CrashLog.Install();   // a crash leaves its stack trace in crash.log
        return AppBuilder.Configure<AiStackprintApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
