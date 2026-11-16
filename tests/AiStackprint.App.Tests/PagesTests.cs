// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia;
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
                new Item("Command", "ship", "Ship it", "Claude Code · git-helper", "User", Path.Combine(_tmp, "plugin", "commands", "ship.md")) { App = "claude-code", Plugin = "git-helper" },
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
            ("Commands & agents", () => new LibraryView(x, "Part").Build()),
            ("MCP servers", () => new McpView(x).Build()),
            ("Usage", () => new UsageView(x).Build()),
            ("Models", () => new ModelsView(x).Build()),
            ("Logs", () => new LogsView(x).Build(null)),
            ("Health", () => new HealthView(x).Build()),
            ("demo-project", () => new ProjectView(x, _ => { }, _ => { }).Build(s.Projects[0])),
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
    public void Author_bars_line_up_including_Other()
    {
        var s = State();
        s.Items = [.. Enumerable.Range(0, 12).Select(i => Skill("s" + i, "User", ["Agent"]) with { Author = "Author " + i })];
        var (w, x) = Host(s);
        w.Content = DashboardView.Build(x, () => { });
        Settle();
        var labels = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is { } n && (n.StartsWith("Author ", StringComparison.Ordinal) || n == "Other")).ToList();
        Assert.Contains(labels, l => l.Text == "Other");
        var left = labels.Select(l => l.TranslatePoint(default, w)!.Value.X).Distinct().ToList();
        Assert.Single(left);   // the "Other" row opens nothing, but sits where the others do
        w.Close();
        Settle();
    }

    [AvaloniaFact]
    public void A_project_folder_that_is_gone_offers_a_new_location_or_removal()
    {
        var s = State();
        var (w, x) = Host(s);
        var gone = Path.Combine(_tmp, "moved-away");
        string? relocated = null, removed = null;
        w.Content = new ProjectView(x, p => removed = p, p => relocated = p).Build(gone);
        Settle();
        Assert.Contains("Folder not found", Ui.Texts(w));
        Button With(string text) => w.GetVisualDescendants().OfType<Button>().First(b => Ui.Texts(b).Contains(text));   // icon buttons: the text is inside
        Ui.Click(With("Choose new location"));
        Ui.Click(With("Remove from list"));
        Assert.Equal(gone, relocated);
        Assert.Equal(gone, removed);
        w.Close();
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

    [AvaloniaFact]
    public void A_left_page_is_freed_and_the_next_visit_keeps_the_search_and_filters()
    {
        var (w, x) = Host(State());
        var skills = new LibraryView(x, "Skill");
        var (page, box) = FirstVisit(w, skills);

        w.Content = new TextBlock { Text = "another page" };   // what MainWindow does when another page opens
        skills.Leave();
        Free(w);
        Assert.False(page.IsAlive);   // nothing the view keeps holds the old page
        Assert.False(box.IsAlive);

        Show(w, skills.Build());
        Assert.Equal("review", SearchBox(w).Text);
        Assert.Contains("1 of 3", Ui.Texts(w));
        Assert.True(Radio(w, "Agent only").IsChecked);
        w.Close();
        Settle();
    }

    /// <summary>The Skills page with a search and a filter; only weak references come back, so the test itself keeps nothing of it.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Page, WeakReference Box) FirstVisit(Window w, LibraryView skills)
    {
        skills.Filter(null, "review");   // as the dashboard does (headless timers do not run, so typing would not search)
        var page = Show(w, skills.Build());
        Assert.Contains("1 of 3", Ui.Texts(w));
        Radio(w, "Agent only").IsChecked = true;
        Settle();
        return (new WeakReference(page), new WeakReference(SearchBox(w)));
    }

    private static TextBox SearchBox(Window w) => w.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText?.StartsWith("Search", StringComparison.Ordinal) == true);

    private static Control Show(Window w, Control page)
    {
        w.Content = page;
        Settle();
        return page;
    }

    /// <summary>Renders a frame (the drawing tree lets go of what was removed) and collects garbage.</summary>
    private static void Free(Window w)
    {
        for (var i = 0; i < 3; i++)
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Settle();
#pragma warning disable S1215 // the test checks that the old page can be collected
            GC.Collect();
#pragma warning restore S1215
            GC.WaitForPendingFinalizers();
        }

        GC.KeepAlive(w);
    }

    private static RadioButton Radio(Window w, string label) =>
        w.GetVisualDescendants().OfType<RadioButton>().First(r => r.Content is string t && t.StartsWith(label + " ", StringComparison.Ordinal));
}
