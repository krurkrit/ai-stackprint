// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>
/// Screenshots of the app: every page and the main features, light and dark, drawn by the real theme on the headless Skia platform.
/// They are written only when the environment variable <c>STACKPRINT_SHOTS</c> names a folder (build/test-and-capture.ps1 sets it); otherwise these tests do nothing.
/// Each shot also checks that the page finished loading, so a page stuck on its loading icon fails here.
/// </summary>
public sealed partial class ScreenshotTests : IDisposable
{
    private static readonly string? _out = Environment.GetEnvironmentVariable("STACKPRINT_SHOTS");
    private static readonly string[] _themes = ["Light", "Dark"];
    private readonly string _tmp = Directory.CreateTempSubdirectory("aishots-").FullName;

    public void Dispose()
    {
        Theme.Appearance(Application.Current!, SettingsStore.Defaults());
        Directory.Delete(_tmp, true);
    }

    private static void Style(string theme, Action<Settings>? change = null)
    {
        var s = SettingsStore.Defaults();
        s.Theme = theme;
        change?.Invoke(s);
        Theme.Appearance(Application.Current!, s);
    }

    private static void Settle() => Dispatcher.UIThread.RunJobs();

    private static bool Loading(Window w) => w.GetVisualDescendants().Any(v => v.Classes.Contains(Busy.Class) && v.IsEffectivelyVisible);

    /// <summary>Runs the UI until no loading icon has shown for 300 ms (a delayed one appears only after a moment).</summary>
    private static void WaitLoaded(Window w)
    {
        Settle();
        var idle = DateTime.MaxValue;
        Ui.WaitUntil(() =>
        {
            if (Loading(w)) { idle = DateTime.MaxValue; }
            else if (idle == DateTime.MaxValue) { idle = DateTime.UtcNow; }
            return DateTime.UtcNow - idle > TimeSpan.FromMilliseconds(300);
        }, 15000);
    }

    private static void Shot(Window w, string name)
    {
        WaitLoaded(w);
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Directory.CreateDirectory(_out!);
#pragma warning disable CS0618 // the options overload has no public encoder class in this Avalonia version; Save(path) writes a PNG
        frame.Save(Path.Combine(_out!, name + ".png"));
#pragma warning restore CS0618
    }

    /// <summary>A window on the theme's main background, so a page looks as it does next to the side menu.</summary>
    private static Window Open(Control content, double width = 1280, double height = 900)
    {
        var w = new Window
        {
            Width = width,
            Height = height,
            Content = new Border { Child = content }.Themed(Border.BackgroundProperty, W.MainKey)
        };
        w.Show();
        Settle();
        return w;
    }

    private AppState State()
    {
        var project = Directory.CreateDirectory(Path.Combine(_tmp, "demo-project")).FullName;
        File.WriteAllText(Path.Combine(project, "AGENTS.md"), "# Rules\nUse tabs.\n");
        var s = new AppState
        {
            Defs =
            [
                new AppDefinition
                {
                    Id = "claude-code",
                    Name = "Claude Code",
                    Category = "agent"
                },
                new AppDefinition
                {
                    Id = "gemini-cli",
                    Name = "Gemini CLI",
                    Category = "cli"
                },
                new AppDefinition
                {
                    Id = "cursor",
                    Name = "Cursor",
                    Category = "ide"
                },
            ],
            Apps =
            [
                new AppInfo("claude-code", "Claude Code", "agent", null, true, null),
                new AppInfo("gemini-cli", "Gemini CLI", "cli", null, true, null),
                new AppInfo("cursor", "Cursor", "ide", null, false, null),
            ],
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
        s.ProjectHealth = [];
        s.Projects.Add(project);
        return s;
    }

    private Item Skill(string name, string level, IReadOnlyList<string> invokers) =>
        new("Skill", name, name + " skill", "Claude Code", level, Path.Combine(_tmp, name, "SKILL.md"))
        {
            App = "claude-code",
            Invokers = invokers
        };

    private static Ctx Context(AppState s, Window? owner = null) => new(s, owner ?? new Window(), _ => { }, () => { }) { Limits = new LimitsService(() => s.Settings) };

    [AvaloniaFact]
    public void Every_page_in_light_and_dark()
    {
        Assert.SkipUnless(_out != null, "Set STACKPRINT_SHOTS to a folder to write screenshots.");
        var s = State();
        var x = Context(s);
        var pages = new (string Name, Func<Control> Build, double Height)[]
        {
            ("dashboard", () => DashboardView.Build(x), 1100),
            ("apps", () => new AppsView(x).Build(), 900),
            ("app-claude-code", () => new AppDetailView(x).Build("claude-code"), 1000),
            ("plugins", () => new LibraryView(x, "Plugin").Build(), 900),
            ("skills", () => new LibraryView(x, "Skill").Build(), 900),
            ("commands", () => new LibraryView(x, "Command").Build(), 900),
            ("agents", () => new LibraryView(x, "Agent").Build(), 900),
            ("servers", () => new McpView(x).Build(), 900),
            ("hooks", () => new HooksView(x).Build(), 900),
            ("usage", () => new UsageView(x).Build(), 1000),
            ("models", () => new ModelsView(x).Build(), 1000),
            ("logs", () => new LogsView(x).Build(null), 900),
            ("health", () => new HealthView(x).Build(), 900),
            ("project", () => new ProjectView(x, _ => { }, _ => { }).Build(s.Projects[0]), 1400),
            ("settings", () => new SettingsView(x).Build(), 2600),
        };
        foreach (var theme in _themes)
        {
            Style(theme);
            foreach (var (name, build, height) in pages)
            {
                var w = Open(build(), 1280, height);
                Shot(w, $"page-{name}-{theme.ToLowerInvariant()}");
                w.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Font_settings_in_their_states()
    {
        Assert.SkipUnless(_out != null, "Set STACKPRINT_SHOTS to a folder to write screenshots.");
        foreach (var theme in _themes)
        {
            Style(theme);
            var set = new FontRuleSet(SettingsStore.Defaults().Fonts);
            var rows = new StackPanel
            {
                Spacing = Space.L,
                Margin = new Thickness(Space.XXL),
                Width = 760
            };
            var controls = new List<FontSetting>();
            foreach (var role in FontRoles.All)
            {
                var c = new FontSetting(set, role.Key, ["Inter", "Roboto", "Noto Sans", "JetBrains Mono", "Fira Code"], edited => controls.Where(o => o != edited).ToList().ForEach(o => o.Refresh()));
                controls.Add(c);
                rows.Children.Add(W.Row(W.Txt(role.Label, TextSize.Body, false, W.Muted).With(t => t.Width = Widths.Label), c.View));
            }

            var w = Open(rows, 900, 420);
            Shot(w, $"fonts-default-{theme.ToLowerInvariant()}");

            set.SetMode(FontRoles.Emphasis, FontProp.Weight, FontMode.Set);      // own weight
            set.SetMode(FontRoles.Menu, FontProp.Size, FontMode.Adjust);         // adjusted size (the offset)
            set.SetNumber(FontRoles.Menu, FontProp.Size, 2);
            set.SetParent(FontRoles.Heading, "");                                 // no parent: every field its own value
            controls.ForEach(c => c.Refresh());
            Shot(w, $"fonts-edited-{theme.ToLowerInvariant()}");
            w.Close();
        }
    }
}
