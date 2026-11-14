// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class UsageReportTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiusage-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private AppDefinition App(string id, string model, int input, int output)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_tmp, id)).FullName;
        var t = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        File.WriteAllText(Path.Combine(dir, "s.jsonl"),
            $"{{\"type\":\"assistant\",\"timestamp\":\"{t}\",\"cwd\":\"/w/proj\",\"message\":{{\"id\":\"{id}1\",\"model\":\"{model}\",\"usage\":{{\"input_tokens\":{input},\"output_tokens\":{output}}},\"content\":[]}}}}\n");
        return new AppDefinition { Id = id, Name = id, Logs = { new LogSource { Path = dir, Pattern = "*.jsonl", Format = "claude-session" } } };
    }

    [Fact]
    public void Usage_is_summed_per_app_ordered_by_tokens_and_apps_without_logs_are_left_out()
    {
        var small = App("small", "claude-opus-5-5", 10, 5);
        var big = App("big", "no-such-model-xyz", 1000, 500);
        var none = new AppDefinition { Id = "none", Name = "None" };
        var report = UsageReport.Compute([small, none, big], [], DateTime.Today.AddDays(-1), TestContext.Current.CancellationToken);
        Assert.Equal(["big", "small"], report.Select(a => a.App.Id));
        Assert.Equal(1500, report[0].Tokens);
        Assert.Null(report[0].Cost);   // no price for that model
        Assert.True(UsageStats.Supports(big));
        Assert.False(UsageStats.Supports(none));
    }

    [Fact]
    public void A_period_in_the_future_has_no_usage()
    {
        Assert.Empty(UsageReport.Compute([App("a", "m", 1, 1)], [], DateTime.Today.AddDays(3), TestContext.Current.CancellationToken));
    }
}
