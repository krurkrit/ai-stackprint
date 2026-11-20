// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Every page, built from made-up scan results: it shows its title and every loading icon goes away.</summary>
public sealed partial class PagesTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aipages-").FullName;

    public void Dispose() => Directory.Delete(_tmp, true);

    private AppState State()
    {
        var project = Directory.CreateDirectory(Path.Combine(_tmp, "demo-project")).FullName;
        File.WriteAllText(Path.Combine(project, "AGENTS.md"), "# Rules\nUse tabs.\n");
        var s = new AppState
        {
            Defs = [new AppDefinition
            {
                Id = "claude-code",
                Name = "Claude Code",
                Category = "agent"
            }],
            Apps = [new AppInfo("claude-code", "Claude Code", "agent", null, true, null)],
            Items =
            [
                Skill("pdf-tools", "User", ["Agent", "User"]),
                Skill("review", "User", ["Agent"]),
                Skill("deploy", "Project", ["User"]),
                new Item("Plugin", "git-helper", "Git tools", "Claude Code", "User", Path.Combine(_tmp, "plugin")) { App = "claude-code" },
                new Item("Command", "ship", "Ship it", "Claude Code · git-helper", "User", Path.Combine(_tmp, "plugin", "commands", "ship.md"))
                {
                    App = "claude-code",
                    Plugin = "git-helper"
                },
            ],
            Mcp = [new McpServer("fs-server", "npx -y fs@1.0.0", "/u/.claude.json", null, false)
            {
                Apps = ["claude-code"],
                Level = "User"
            }],
            Hooks = [new Hook("Stop", null, "echo done", "command", "/u/.claude/settings.json") { Apps = ["claude-code"] }],
        };
        s.Health = [new HealthIssue("Skills", "review", "claude-code", new Finding("No description.", true), s.Items[1])];
        s.ProjectHealth = [];   // the projects' checks are done (they run after the dashboard shows)
        s.Projects.Add(project);
        return s;
    }

    private Item Skill(string name, string level, IReadOnlyList<string> invokers) =>
        new("Skill", name, name + " skill", "Claude Code", level, Path.Combine(_tmp, name, "SKILL.md"))
        {
            App = "claude-code",
            Invokers = invokers
        };

    private static (Window Window, Ctx Ctx) Host(AppState s)
    {
        var w = new Window
        {
            Width = 1200,
            Height = 800
        };
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
            ("Dashboard", () => DashboardView.Build(x)),
            ("Apps", () => new AppsView(x).Build()),
            ("Claude Code", () => new AppDetailView(x).Build("claude-code")),
            ("Plugins", () => new LibraryView(x, "Plugin").Build()),
            ("Skills", () => new LibraryView(x, "Skill").Build()),
            ("Commands & Agents", () => new LibraryView(x, "Part").Build()),
            ("Servers", () => new McpView(x).Build()),
            ("Hooks", () => new HooksView(x).Build()),
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
            Ui.WaitUntil(() =>
            {
                if (Loading(w)) { idle = DateTime.MaxValue; } else if (idle == DateTime.MaxValue) { idle = DateTime.UtcNow; }
                return DateTime.UtcNow - idle > TimeSpan.FromMilliseconds(300);
            }, 15000);
            Assert.Contains(title, Ui.Texts(w));
        }

        w.Close();   // nothing of this test left running in the next one
        Settle();
    }

    [AvaloniaFact]
    public void A_config_file_says_what_it_holds_and_its_hooks_open_the_Hooks_page()
    {
        var s = State();
        var settings = Path.Combine(_tmp, "settings.json");
        File.WriteAllText(settings, """{ "model": "claude-opus-5-5" }""");
        s.Items = [new Item("Config", "settings.json", "Settings", "Claude Code", "User", settings) { App = "claude-code" }];
        s.Hooks = [new Hook("Stop", null, "echo done", "command", settings) { Apps = ["claude-code"] }];
        var w = new Window
        {
            Width = 1200,
            Height = 800
        };
        string? went = null;
        var x = new Ctx(s, w, v => went = v, () => { }) { Limits = new LimitsService(() => s.Settings) };
        w.Show();
        w.Content = new AppDetailView(x).Build("claude-code");
        Settle();

        Assert.Contains("model claude-opus-5-5", Ui.Texts(w));
        var link = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "1 hook · ");
        Assert.Equal("Open the Hooks page", ToolTip.GetTip(link));
        link.RaiseEvent(new PointerReleasedEventArgs(link, new Pointer(1, PointerType.Mouse, true), w, default, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Assert.Equal("Hooks", went);
        w.Close();
        Settle();
    }

    [AvaloniaFact]
    public void Start_in_offers_the_folders_with_their_own_instruction_files()
    {
        var s = State();
        s.Defs = [new AppDefinition
        {
            Id = "codex",
            Name = "Codex",
            Category = "agent",
            Context = "codex",
            Configs = { new ConfigFile
            {
                Path = "AGENTS.md",
                Level = "Project",
                Kind = "instructions"
            } }
        }];
        s.Apps = [new AppInfo("codex", "Codex", "agent", null, true, null)];
        Directory.CreateDirectory(Path.Combine(s.Projects[0], "svc", "api"));
        File.WriteAllText(Path.Combine(s.Projects[0], "svc", "api", "AGENTS.md"), "api rules");
        var w = new Window
        {
            Width = 1200,
            Height = 800
        };
        ContextSection? section = null;
        var x = new Ctx(s, w, _ => { }, () => { }) { Refresh = () => w.Content = section!.Build(s.Projects[0]) };
        section = new ContextSection(x);
        w.Show();
        x.Refresh();
        Settle();

        Ui.Click(w.GetVisualDescendants().OfType<Button>().First(b => Ui.Texts(b).Contains("Start in…")));   // a button with an icon
        Settle();
        var items = TopLevel.GetTopLevel(w)!.GetVisualDescendants().Concat(Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(w)?.GetVisualDescendants() ?? [])
            .OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.Equal(["svc/api", "Other folder…"], items);
        w.Close();
        Settle();
    }

    [AvaloniaFact]
    public void Author_bars_line_up_including_Other()
    {
        var s = State();
        s.Items = [.. Enumerable.Range(0, 12).Select(i => Skill("s" + i, "User", ["Agent"]) with { Author = "Author " + i })];
        var (w, x) = Host(s);
        w.Content = DashboardView.Build(x);
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
}
