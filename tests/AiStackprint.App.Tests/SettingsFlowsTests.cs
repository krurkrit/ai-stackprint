// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text;

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>A stand-in for the network: the feature gets an HttpClient that answers from a function, so nothing leaves the machine.</summary>
internal sealed class FakeNetwork(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(answer(request));
    }

    public static HttpResponseMessage Reply(HttpStatusCode code, string body = "{}") => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>The Settings page on a made-up home: the update, model data and benchmark downloads answer from fake networks, and every switch works.</summary>
public sealed class SettingsFlowsTests : IDisposable
{
    private readonly FakeHome _home = new();
    private readonly HttpClient _updates = UpdateCheck.Http, _models = ModelDataUpdate.Http, _benchmarks = BenchmarkUpdate.Http;

    public void Dispose()
    {
        UpdateCheck.Http = _updates;
        ModelDataUpdate.Http = _models;
        BenchmarkUpdate.Http = _benchmarks;
        _home.Dispose();
        Toast.Host = null;
    }

    private static void Settle(Window w)
    {
        var idle = DateTime.MaxValue;
        Ui.WaitUntil(() =>
        {
            if (w.GetVisualDescendants().Any(v => v.Classes.Contains(Ui.BusyClass) && v.IsEffectivelyVisible)) { idle = DateTime.MaxValue; }
            else if (idle == DateTime.MaxValue) { idle = DateTime.UtcNow; }
            return DateTime.UtcNow - idle > TimeSpan.FromMilliseconds(400);
        }, 20000);
    }

    private static Window OpenSettings(params string[] pages)
    {
        var w = new MainWindow { Height = 3200 };
        w.Show();
        Settle(w);
        Ui.Click(w.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Settings")));
        Settle(w);
        foreach (var page in pages)
        {
            Press(w, page);   // the side menu lists the subpages while Settings is open
        }

        return w;
    }

    private static void Press(Window w, string text)
    {
        Ui.Click(w.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && (b.Content as string == text || b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text))));
        Settle(w);
    }

    private static ToggleSwitch Toggle(Window w, string content) =>
        w.GetVisualDescendants().OfType<ToggleSwitch>().First(t => t.Content as string == content || t.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == content));

    [AvaloniaFact]
    public void The_update_model_data_and_benchmark_checks_report_what_the_network_said()
    {
        var release = new FakeNetwork(_ => FakeNetwork.Reply(HttpStatusCode.OK, """{ "tag_name": "v9.9.9", "html_url": "https://github.com/krurkrit/ai-stackprint/releases/tag/v9.9.9" }"""));
        var gone = new FakeNetwork(_ => FakeNetwork.Reply(HttpStatusCode.ServiceUnavailable));
        var refuse = new FakeNetwork(_ => FakeNetwork.Reply(HttpStatusCode.Unauthorized));
        UpdateCheck.Http = new HttpClient(release);
        ModelDataUpdate.Http = new HttpClient(gone);
        BenchmarkUpdate.Http = new HttpClient(refuse);
        var w = OpenSettings("Updates");

        Toggle(w, "Check GitHub for updates and model data once a day").IsChecked = true;
        Settle(w);
        Press(w, "Check now");
        Assert.Contains(Ui.Texts(w), t => t.Contains("9.9.9", StringComparison.Ordinal));
        Assert.True(release.Calls >= 1);

        Press(w, "Check model data now");
        Assert.True(gone.Calls >= 1);
        Assert.Contains(Ui.Texts(w), t => t.Contains("503", StringComparison.Ordinal));

        Press(w, "Benchmarks");
        Toggle(w, "Download benchmark scores once a day").IsChecked = true;
        Settle(w);
        var key = w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.PasswordChar != default);
        key.Text = "secret-key";
        Press(w, "Save key");
        Press(w, "Refresh now");
        Assert.True(refuse.Calls >= 1);
        Assert.Contains(Ui.Texts(w), t => t.Contains("does not accept this key", StringComparison.Ordinal));
        Assert.DoesNotContain(Ui.Texts(w), t => t.Contains("secret-key", StringComparison.Ordinal));   // a saved key is never shown again
        Press(w, "Remove key");
        w.Close();
    }

    [AvaloniaFact]
    public void Every_switch_and_reset_on_the_page_works()
    {
        var w = OpenSettings();
        foreach (var t in w.GetVisualDescendants().OfType<ToggleSwitch>().Where(t => t.IsEffectivelyVisible && t.Content is null).Take(8).ToList())
        {
            t.IsChecked = !t.IsChecked;
            Dispatcher.UIThread.RunJobs();
        }

        Press(w, "Reset level colors");
        Press(w, "Reset to defaults");
        Assert.Contains("Appearance", Ui.Texts(w));
        Press(w, "Apps");
        Toggle(w, "Hide apps that are not installed").IsChecked = true;
        Settle(w);
        w.Close();
    }

    [AvaloniaFact]
    public void Settings_lists_its_subpages_in_the_side_menu_and_Back_to_app_returns()
    {
        var w = OpenSettings();
        var seen = Ui.Texts(w);
        foreach (var name in new[] { "Back to app", "Appearance", "Apps", "Usage limits", "Updates", "Benchmarks", "Export and import", "About" })
        {
            Assert.Contains(name, seen);
        }

        Press(w, "About");
        Assert.Contains(Ui.Texts(w), t => t.Contains("MIT License", StringComparison.Ordinal));
        Press(w, "Back to app");
        Assert.Contains("Health", Ui.Texts(w));
        Assert.DoesNotContain("Back to app", Ui.Texts(w));
        w.Close();
    }
}
