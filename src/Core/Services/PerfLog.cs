// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AiStackprint.Services;

/// <summary>
/// One page (or the start) in a performance run: how long building it took on the UI thread, how long until every loading icon was gone,
/// the CPU time the process used meanwhile, and the memory after it.
/// </summary>
public sealed record PerfRow(DateTime At, string Version, string Page, long BuildMs, long ReadyMs, long CpuMs, double HeapMb, double WorkingSetMb);

/// <summary>
/// perf.log: the rows of every performance run (<c>AiStackprint --perf</c>), one CSV line each, kept so runs can be compared over time.
/// Only page names, times and memory: no paths, names or file content.
/// </summary>
public static class PerfLog
{
    internal const string Header = "at,version,page,build_ms,ready_ms,cpu_ms,heap_mb,working_set_mb";
    private static readonly CultureInfo _inv = CultureInfo.InvariantCulture;

    public static string File => Path.Combine(DataFolder.Logs, "perf.log");
    public static string Report => Path.Combine(DataFolder.Logs, "perf-report.txt");

    /// <summary>
    /// The process now: CPU time used so far, managed heap and working set in MB. <paramref name="live"/>: collect first, so the heap is what is still held
    /// (a page that keeps memory after it closes shows here), not garbage waiting for the next collection.
    /// </summary>
    public static (TimeSpan Cpu, double HeapMb, double WorkingSetMb) Sample(bool live)
    {
#pragma warning disable S1215 // a measuring run only (--perf): the collection is what makes the number the memory still held
        var heap = GC.GetTotalMemory(live);
#pragma warning restore S1215
        using var p = Process.GetCurrentProcess();
        return (p.TotalProcessorTime, heap / 1048576.0, p.WorkingSet64 / 1048576.0);
    }

    public static (TimeSpan Cpu, double HeapMb, double WorkingSetMb) Sample() => Sample(false);

    public static void Append(IEnumerable<PerfRow> rows, string? file)
    {
        var path = file ?? File;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        if (!System.IO.File.Exists(path))
        {
            sb.AppendLine(Header);
        }

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", r.At.ToString("s", _inv), r.Version, r.Page.Replace(",", " ", StringComparison.Ordinal), r.BuildMs.ToString(_inv), r.ReadyMs.ToString(_inv),
                r.CpuMs.ToString(_inv), r.HeapMb.ToString("0.0", _inv), r.WorkingSetMb.ToString("0.0", _inv)));
        }

        System.IO.File.AppendAllText(path, sb.ToString());
    }

    public static void Append(IEnumerable<PerfRow> rows) => Append(rows, null);

    /// <summary>Every row in the log; lines that do not parse are skipped.</summary>
    public static List<PerfRow> Read(string? file)
    {
        var path = file ?? File;
        if (!System.IO.File.Exists(path))
        {
            return [];
        }

        var res = new List<PerfRow>();
        foreach (var line in System.IO.File.ReadLines(path).Skip(1))
        {
            var c = line.Split(',');
            if (c.Length == 8 && DateTime.TryParse(c[0], _inv, DateTimeStyles.None, out var at) && long.TryParse(c[3], _inv, out var b) && long.TryParse(c[4], _inv, out var r)
                && long.TryParse(c[5], _inv, out var cpu) && double.TryParse(c[6], _inv, out var heap) && double.TryParse(c[7], _inv, out var ws))
            {
                res.Add(new PerfRow(at, c[1], c[2], b, r, cpu, heap, ws));
            }
        }

        return res;
    }

    public static List<PerfRow> Read() => Read(null);

    /// <summary>
    /// The newest run (rows with the newest time stamp's run, written together) next to the run before it, page by page:
    /// a plain-text table with the change in time until ready.
    /// </summary>
    public static string Compare(List<PerfRow> rows)
    {
        var runs = rows.GroupBy(r => r.At).OrderBy(g => g.Key).ToList();
        if (runs.Count == 0)
        {
            return "No performance runs yet.";
        }

        var last = runs[^1].ToList();
        var before = runs.Count > 1 ? runs[^2].ToDictionary(r => r.Page) : [];
        var sb = new StringBuilder();
        var title = string.Create(_inv, $"Run {runs[^1].Key:yyyy-MM-dd HH:mm} ({last[0].Version})");
        if (runs.Count > 1)
        {
            title += string.Create(_inv, $", compared with {runs[^2].Key:yyyy-MM-dd HH:mm} ({runs[^2].First().Version})");
        }

        sb.AppendLine(title);
        sb.AppendLine(CultureInfo.InvariantCulture, $"{"Page",-16}{"Build ms",10}{"Ready ms",10}{"Change",10}{"CPU ms",10}{"Heap MB",10}{"WS MB",10}");
        foreach (var r in last)
        {
            var change = before.TryGetValue(r.Page, out var b) ? Change(b.ReadyMs, r.ReadyMs) : "new";
            sb.AppendLine(CultureInfo.InvariantCulture, $"{r.Page,-16}{r.BuildMs,10}{r.ReadyMs,10}{change,10}{r.CpuMs,10}{r.HeapMb,10:0.0}{r.WorkingSetMb,10:0.0}");
        }

        return sb.ToString();
    }

    private static string Change(long before, long now) =>
        before == 0 ? (now == 0 ? "0%" : "new") : ((now - before) * 100.0 / before).ToString("+0;-0;0", _inv) + "%";
}
