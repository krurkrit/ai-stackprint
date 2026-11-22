// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>
/// The --perf run's heaviest work, after every page: the largest session log on this machine opened in Logs, its whole session searched, then every
/// file of its app searched; last, the most memory the process used during the run (the report says when it went over <see cref="PerfLog.MemoryLimitMb"/>).
/// </summary>
internal partial class MainWindow
{
    private const string PerfSearch = "the";   // a common word: many results to list

    private async Task PerfLargestSession(DateTime at, string version, List<PerfRow> rows)
    {
        var defs = _s.Defs;
        var projects = _s.Projects.ToList();
        var largest = await Task.Run(() => defs
            .SelectMany(d => LogReader.Find(d, projects).Where(f => LogParsers.Find(f.Format)?.Feed() != null).Select(f => (Def: d, File: f)))
            .OrderByDescending(x => x.File.Size)
            .FirstOrDefault());
        if (largest.File is not { } file)
        {
            return;   // no session logs on this machine
        }

        await PerfStep(at, version, rows, "Session: open", async () =>
        {
            Go("Logs:" + largest.Def.Id);
            await Ready();
            _logs.OpenForPerf(largest.Def, file);
        });
        await PerfStep(at, version, rows, "Session: search", () =>
        {
            _logs.SearchForPerf(PerfSearch, false);
            return Task.CompletedTask;
        });
        await PerfStep(at, version, rows, "Session: all files", () =>
        {
            _logs.SearchForPerf(PerfSearch, true);
            return Task.CompletedTask;
        });
        _logs.SearchForPerf("", true);   // leaves the box empty, as the run found it
    }

    private async Task PerfStep(DateTime at, string version, List<PerfRow> rows, string name, Func<Task> start)
    {
        var before = PerfLog.Sample();
        var sw = Stopwatch.StartNew();
        await start();
        var build = sw.ElapsedMilliseconds;
        await Ready();
        var ready = sw.ElapsedMilliseconds;
        var after = PerfLog.Sample(live: true);
        rows.Add(new PerfRow(at, version, name, build, ready, (long)(after.Cpu - before.Cpu).TotalMilliseconds, after.HeapMb, after.WorkingSetMb));
    }

    /// <summary>The most memory (working set) the process has used since it started.</summary>
    private static PerfRow PerfPeak(DateTime at, string version)
    {
        using var p = Process.GetCurrentProcess();
        return new PerfRow(at, version, PerfLog.PeakPage, 0, 0, 0, 0, p.PeakWorkingSet64 / 1048576.0);
    }
}
