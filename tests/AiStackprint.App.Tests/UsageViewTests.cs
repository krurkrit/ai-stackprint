// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class UsageViewTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aiusageui-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    [AvaloniaFact]
    public void The_page_shows_totals_and_a_row_per_app_with_usage()
    {
        var t = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        File.WriteAllText(Path.Combine(_tmp, "s.jsonl"), $"{{\"type\":\"assistant\",\"timestamp\":\"{t}\",\"cwd\":\"/w/p\",\"message\":{{\"id\":\"m1\",\"model\":\"x-model\",\"usage\":{{\"input_tokens\":1500,\"output_tokens\":500}},\"content\":[]}}}}\n");
        var def = new AppDefinition { Id = "demo", Name = "Demo App", Logs = { new LogSource { Path = _tmp, Pattern = "*.jsonl", Format = "claude-session" } } };
        var s = new AppState { Defs = [def], Apps = [new AppInfo("demo", "Demo App", "agent", null, true, null)] };
        var w = new Window { Width = 1200, Height = 900 };
        var x = new Ctx(s, w, _ => { }, () => { });
        var view = new UsageView(x);
        w.Content = view.Build();
        w.Show();
        Ui.WaitUntil(() => Ui.Texts(w).Contains("By app"));
        var texts = Ui.Texts(w);
        Assert.Contains("Demo App", texts);
        Assert.Contains("1.5K", texts);   // input tile
        Assert.Contains("Sessions", texts);
        Assert.Contains("By model", texts);
        Assert.Contains("x-model", texts);
        Dispatcher.UIThread.RunJobs();
    }
}
