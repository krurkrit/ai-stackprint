// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class PerfLogTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiperf-").FullName;
    private string Log => Path.Combine(_tmp, "perf.log");

    public void Dispose() => Directory.Delete(_tmp, true);

    private static PerfRow Row(DateTime at, string page, long ready) => new(at, "1.0.0", page, ready / 2, ready, ready * 2, 12.5, 200.5);

    [Fact]
    public void Append_writes_the_header_once_and_Read_gives_the_rows_back()
    {
        var at = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Local);
        PerfLog.Append([Row(at, "Skills", 400), Row(at, "Scan: cache, save", 30)], Log);
        PerfLog.Append([Row(at.AddHours(1), "Skills", 200)], Log);
        File.AppendAllText(Log, "not,a,row\n");

        var lines = File.ReadAllLines(Log);
        Assert.Equal(PerfLog.Header, lines[0]);
        Assert.Single(lines, l => l == PerfLog.Header);
        var rows = PerfLog.Read(Log);
        Assert.Equal(3, rows.Count);   // the broken line is skipped
        Assert.Equal(Row(at, "Skills", 400), rows[0]);
        Assert.Equal("Scan: cache  save", rows[1].Page);   // a comma in a name cannot break the columns
    }

    [Fact]
    public void The_report_says_whether_the_run_stayed_under_the_memory_limit()
    {
        var at = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Local);
        Assert.Contains("Memory stayed under 500 MB (most: 201 MB)", PerfLog.Compare([Row(at, "Skills", 400)]));
        var over = PerfLog.Compare([Row(at, "Skills", 400), new(at, "1.0.0", PerfLog.PeakPage, 0, 0, 0, 0, 612)]);
        Assert.Contains("Over the 500 MB memory limit: Peak memory 612 MB", over);
    }

    [Fact]
    public void Compare_puts_the_newest_run_next_to_the_one_before()
    {
        var first = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Local);
        var report = PerfLog.Compare([Row(first, "Skills", 400), Row(first, "Logs", 100), Row(first.AddHours(1), "Skills", 300), Row(first.AddHours(1), "Usage", 50)]);

        Assert.Contains("Run 2026-11-15 11:00 (1.0.0), compared with 2026-11-15 10:00 (1.0.0)", report);
        Assert.Contains(report.Split('\n'), l => l.StartsWith("Skills", StringComparison.Ordinal) && l.Contains("-25%"));
        Assert.Contains(report.Split('\n'), l => l.StartsWith("Usage", StringComparison.Ordinal) && l.Contains("new"));
        Assert.DoesNotContain("Logs", report);   // only pages of the newest run
        Assert.Equal("No performance runs yet.", PerfLog.Compare([]));
    }

    [Fact]
    public void A_sample_has_the_cpu_time_and_memory_and_the_data_folder_log_is_used_without_a_file()
    {
        var (cpu, heap, ws) = PerfLog.Sample(live: true);
        Assert.True(cpu > TimeSpan.Zero && heap > 0 && ws >= heap);
        Assert.True(PerfLog.Sample().HeapMb > 0);
        var at = new DateTime(2026, 11, 22, 9, 0, 0, DateTimeKind.Local);
        PerfLog.Append([Row(at, "Test page", 5)]);
        Assert.Contains(PerfLog.Read(), r => r.Page == "Test page" && r.At == at);
        Assert.EndsWith("perf-report.txt", PerfLog.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_that_do_not_parse_are_skipped()
    {
        File.WriteAllText(Log, PerfLog.Header + "\nnot,a,row\n2026-11-22T09:00:00,1.0,Skills,x,2,3,4.0,5.0\n2026-11-22T09:00:00,1.0,Skills,1,2,3,4.0,5.0\n");
        Assert.Single(PerfLog.Read(Log));
    }
}
