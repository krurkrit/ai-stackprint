// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Every page, built from made-up scan results: it shows its title and every loading icon goes away.</summary>
public sealed class PagesTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aipages-").FullName;

    public void Dispose() => Directory.Delete(_tmp, true);

    private AppState State()
    {
        var project = Directory.CreateDirectory(Path.Combine(_tmp, "demo-project")).FullName;
        File.WriteAllText(Path.Combine(project, "AGENTS.md"), "# Rules\nUse tabs.\n");
        var s = new AppState
        {
            Defs = [new AppDefinition { Id = "claude-code", Name = "Claude Code", Category = "agent" }],
            Apps = [new AppInfo("claude-code", "Claude Code", "agent", null, true, null)],
            Items =
            [
                Skill("pdf-tools", "User", ["Agent", "User"]),
                Skill("review", "User", ["Agent"]),
                Skill("deploy", "Project", ["User"]),
                new Item("Plugin", "git-helper", "Git tools", "Claude Code", "User", Path.Combine(_tmp, "plugin")) { App = "claude-code" },
            ],
            Mcp = [new McpServer("fs-server", "npx -y fs@1.0.0", "/u/.claude.json", null, false) { Apps = ["claude-code"], Level = "User" }],
        };
        s.Health = [new HealthIssue("Skills", "review", "claude-code", new Finding("No description.", true), s.Items[1])];
        s.Projects.Add(project);
        return s;
    }

    private Item Skill(string name, string level, IReadOnlyList<string> invokers) =>
        new("Skill", name, name + " skill", "Claude Code", level, Path.Combine(_tmp, name, "SKILL.md")) { App = "claude-code", Invokers = invokers };

    private static (Window Window, Ctx Ctx) Host(AppState s)
    {
        var w = new Window { Width = 1200, Height = 800 };
        var x = new Ctx(s, w, _ => { }, () => { }) { Limits = new LimitsService(() => s.Settings) };
        w.Show();
        return (w, x);
    }

    private static void Settle() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();   // layout and posted work

    private static bool Loading(Window w) => w.GetVisualDescendants().Any(v => v.Classes.Contains(Busy.Class) && v.IsEffectivelyVisible);

    [AvaloniaFact]
    public void Every_page_shows_its_title_and_finishes_loading()
    {
        var s = State();
        var (w, x) = Host(s);
        var pages = new (string Title, Func<Control> Build)[]
        {
            ("Dashboard", () => DashboardView.Build(x, () => { })),
            ("Apps", () => new AppsView(x).Build()),
            ("Claude Code", () => new AppDetailView(x).Build("claude-code")),
            ("Plugins", () => new LibraryView(x, "Plugin").Build()),
            ("Skills", () => new LibraryView(x, "Skill").Build()),
            ("MCP servers", () => new McpView(x).Build()),
            ("Usage", () => new UsageView(x).Build()),
            ("Logs", () => new LogsView(x).Build(null)),
            ("Health", () => new HealthView(x).Build()),
            ("demo-project", () => new ProjectView(x, _ => { }).Build(s.Projects[0])),
            ("Settings", () => new SettingsView(x).Build()),
        };
        foreach (var (title, build) in pages)
        {
            w.Content = build();
            Settle();
            var idle = DateTime.MaxValue;   // a delayed loading icon appears only after a moment: done = none for 300 ms
            Ui.WaitUntil(() => { if (Loading(w)) { idle = DateTime.MaxValue; } else if (idle == DateTime.MaxValue) { idle = DateTime.UtcNow; } return DateTime.UtcNow - idle > TimeSpan.FromMilliseconds(300); }, 15000);
            Assert.Contains(title, Ui.Texts(w));
        }

        w.Close();   // nothing of this test left running in the next one
        Settle();
    }

    [AvaloniaFact]
    public void Skills_count_returns_to_the_total_when_All_is_picked_again_and_follows_the_level_checks()
    {
        var (w, x) = Host(State());
        var skills = new LibraryView(x, "Skill");
        w.Content = skills.Build();
        Settle();
        Assert.Contains("3", Ui.Texts(w));   // the count beside the search: all skills shown

        Radio(w, "Agent only").IsChecked = true;
        Settle();
        Assert.Contains("1 of 3", Ui.Texts(w));

        Radio(w, "All").IsChecked = true;   // the first choice again: the bug left "1 of 3"
        Settle();
        Assert.Contains("3", Ui.Texts(w));
        Assert.DoesNotContain("1 of 3", Ui.Texts(w));

        w.GetVisualDescendants().OfType<CheckBox>().First(c => Ui.Texts(c).Contains("Project")).IsChecked = false;
        Settle();
        Assert.Contains("2 of 3", Ui.Texts(w));
        Assert.Contains("All 2", Ui.Texts(w));   // the radio counts follow the level filter
        w.Close();
        Settle();
    }

    private static RadioButton Radio(Window w, string label) =>
        w.GetVisualDescendants().OfType<RadioButton>().First(r => r.Content is string t && t.StartsWith(label + " ", StringComparison.Ordinal));
}
