using System.Reflection;

namespace AiStackprint.Services;

/// <summary>
/// Writes errors that would close the app (and errors in background work nobody waited for) to crash.log next to settings.json,
/// so a crash can be investigated. It only records: the app still closes as before. Kept under 256 KB.
/// </summary>
public static class CrashLog
{
    const long MaxBytes = 256 * 1024;
    public static string File => Path.Combine(AppCatalog.Folder, "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("The app closed after this error", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Background work failed (the app kept running)", e.Exception);
    }

    /// <param name="folder">Where crash.log goes; the app data folder when null.</param>
    public static void Write(string what, Exception? e, string? folder = null)
    {
        folder ??= AppCatalog.Folder;
        try
        {
            Directory.CreateDirectory(folder);
            var info = new FileInfo(Path.Combine(folder, "crash.log"));
            if (info.Exists && info.Length > MaxBytes) info.Delete();   // start over rather than grow without end
            var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version;   // the app's version, not Core's
            System.IO.File.AppendAllText(info.FullName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · {what} · v{version} · {Environment.OSVersion}\n{e}\n\n");
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }   // nowhere to write: nothing more to do while closing
    }

    /// <summary>True once after crash.log gained an entry the user has not been told about.</summary>
    public static bool TakeNew(string? folder = null)
    {
        folder ??= AppCatalog.Folder;
        string log = Path.Combine(folder, "crash.log"), seen = Path.Combine(folder, "crash.seen");
        try
        {
            if (!System.IO.File.Exists(log)) return false;
            if (System.IO.File.Exists(seen) && System.IO.File.GetLastWriteTimeUtc(seen) >= System.IO.File.GetLastWriteTimeUtc(log)) return false;
            System.IO.File.WriteAllText(seen, "");
            return true;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { return false; }
    }
}
