// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Ui;

using Avalonia.Headless.XUnit;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class LogSummaryCardTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aistack-ui-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    private LogFile Log(string name, string text, DateTime modified)
    {
        var p = Path.Combine(_tmp, name);
        File.WriteAllText(p, text);
        File.SetLastWriteTime(p, modified);
        return new LogFile("app", name, p, new FileInfo(p).Length, modified, null);
    }

    [AvaloniaFact]
    public void Summary_counts_files_and_error_lines_and_follows_the_range()
    {
        var recent = Log("today.log", "start\nERROR disk full\nWARN slow\nok\n", DateTime.Now);
        var old = Log("old.log", "ERROR one\nERROR two\n", DateTime.Now.AddDays(-20));
        var card = new LogSummaryCard();
        var w = Ui.Show(card.Build([recent, old]));

        // 7 days (the default): only today's file
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.StartsWith("1 file", StringComparison.Ordinal)));
        Assert.Contains("1 error line", Ui.Texts(w));
        Assert.Contains("1 warning line", Ui.Texts(w));

        // All: both files, recalculated in the background
        Ui.Click(Ui.Button(w, "All"));
        Ui.WaitUntil(() => Ui.Texts(w).Any(t => t.StartsWith("2 files", StringComparison.Ordinal)));
        Assert.Contains("3 error lines", Ui.Texts(w));
        Assert.Equal(0, card.Days);
    }
}
