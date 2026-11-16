// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

using AiStackprint.Services;

using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AiStackprint.Ui;

/// <summary>
/// <c>AiStackprint --perf</c>: after the first scan, opens every page in turn and records how long it took to build, how long until no loading icon
/// was left, the CPU time used and the memory after it; appends the rows to perf.log, writes perf-report.txt (this run next to the one before) and closes.
/// </summary>
internal partial class MainWindow
{
    private const int PerfTimeoutMs = 60_000, PerfPollMs = 25;

    /// <summary>Started with --perf.</summary>
    public bool PerfRun { get; init; }

    private long _perfOpened, _perfScanMs;   // when the window opened (Stopwatch time stamp); how long the first scan took
    private List<(string Name, long Ms)> _perfSteps = [];   // the first scan's steps

    private async void RunPerf()
    {
        var at = DateTime.Now;   // one time stamp for the whole run: perf-report.txt compares runs by it
        var version = UpdateCheck.Label;
        var rows = new List<PerfRow>();
        var shown = Stopwatch.GetTimestamp();
        await Ready();
        using (var p = Process.GetCurrentProcess())
        {
            var s = PerfLog.Sample(live: true);
            var start = (long)(DateTime.UtcNow - p.StartTime.ToUniversalTime()).TotalMilliseconds;   // the process start has no stopwatch: clock time
            var launch = start - (long)Stopwatch.GetElapsedTime(_perfOpened).TotalMilliseconds;
            rows.Add(new PerfRow(at, version, "Launch", launch, launch, 0, 0, 0));   // until the window opened
            rows.Add(new PerfRow(at, version, "Scan", _perfScanMs, _perfScanMs, 0, 0, 0));   // the first scan and its checks (background)
            rows.AddRange(_perfSteps.Select(t => new PerfRow(at, version, "Scan: " + t.Name, t.Ms, t.Ms, 0, 0, 0)));
            rows.Add(new PerfRow(at, version, "Start", start, start, (long)s.Cpu.TotalMilliseconds, s.HeapMb, s.WorkingSetMb));   // launch until the dashboard is complete (live limits included)
            rows.Add(new PerfRow(at, version, "Dashboard data", 0, (long)Stopwatch.GetElapsedTime(shown).TotalMilliseconds, 0, 0, 0));   // after the scan: until no card is loading
        }

        foreach (var (name, view) in PerfPages())
        {
            var before = PerfLog.Sample();
            var sw = Stopwatch.StartNew();
            Go(view);
            var build = sw.ElapsedMilliseconds;
            await Ready();
            var ready = sw.ElapsedMilliseconds;
            var after = PerfLog.Sample(live: true);
            rows.Add(new PerfRow(at, version, name, build, ready, (long)(after.Cpu - before.Cpu).TotalMilliseconds, after.HeapMb, after.WorkingSetMb));
        }

        try
        {
            PerfLog.Append(rows);
            File.WriteAllText(PerfLog.Report, PerfLog.Compare(PerfLog.Read()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.WriteLine("perf.log not written: " + e.Message); }   // a measuring run only: nothing on screen to tell

        Close();
    }

    /// <summary>Every page, with the first installed app and the first project; Logs twice (opening it, then again with its files known); the dashboard last.</summary>
    private List<(string Name, string View)> PerfPages()
    {
        var pages = new List<(string, string)> { ("Apps", "Apps") };
        if (_s.Apps.FirstOrDefault(a => a.Installed) is { } app)
        {
            pages.Add(("App", "App:" + app.Id));
        }

        pages.AddRange([("Plugins", "Plugin"), ("Skills", "Skill"), ("Commands & agents", "Part"), ("Servers", "Mcp"), ("Hooks", "Hooks"), ("Usage", "Usage"), ("Models", "Models"), ("Logs", "Logs"), ("Health", "Health")]);
        if (_s.Projects.Count > 0)
        {
            pages.Add(("Project", "Project:" + _s.Projects[0]));
        }

        pages.AddRange([("Settings", "Settings"), ("Logs again", "Logs"), ("Dashboard", "Dashboard")]); return pages;
    }

    /// <summary>Waits until the page is laid out and no loading icon is left on it (at most a minute).</summary>
    private async Task Ready()
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < PerfTimeoutMs)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);   // after layout and render
            if (!this.GetVisualDescendants().Any(v => v.Classes.Contains(Busy.Class) && v.IsEffectivelyVisible))
            {
                return;
            }

            await Task.Delay(PerfPollMs);
        }
    }
}
