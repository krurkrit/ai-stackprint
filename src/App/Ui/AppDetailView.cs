// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>One app: status, instruction/settings files with preview, plugins and skills with add/remove.</summary>
internal class AppDetailView(Ctx x)
{
    private string? _preview;   // config file shown in the viewer/editor (click a row)
    private readonly UsageCard.Prefs _usage = new();
    private SkillFilter? _skillFilter;
    private Action _skillFill = () => { };   // the Skills section's Fill this visit
    private TextBox? _skillSearch;
    private string? _skillsApp;   // app the skill filters belong to: reset when another app opens
    private string _skillQuery = "";

    public Control Build(string appId)
    {
        if (x.S.Def(appId) is not { } d)
        {
            return Page(Txt("This app is hidden or no longer defined.", TextSize.Body, false, Muted));
        }

        var info = x.Info(appId);
        var items = x.S.Items.Where(i => i.App == appId).ToList();
        return Page(Header(d, info), LimitsView.Card(x, d), Usage(d), Configs(items), Plugins(d, items), Skills(d, items));
    }

    public static string LevelLabel(Item i) => i.Level is "Project" or "Local" && i.Project != null ? $"{i.Level} · {Ctx.ProjectName(i.Project)}" : i.Level;
    public static Control LevelCell(Item i) => Level(i.Level, LevelLabel(i), i.LevelLabel);

    /// <summary>Where a skill came from: its plugin, the app itself (bundled) or a skills folder.</summary>
    public static string From(Item i) => SkillMatch.From(i);

    /// <summary>Who can start a skill, from its SKILL.md front matter.</summary>
    public static Control InvokeCell(Item i)
    {
        var label = SkillMatch.Label(i);
        var tip = label switch
        {
            "User only" => "disable-model-invocation: true — runs only when you invoke it (e.g. /name)",
            "Agent only" => "user-invocable: false — the agent loads it when relevant; hidden from your command list",
            _ => "The agent loads it when relevant, and you can invoke it yourself"
        };
        return Txt(label, TextSize.Small).With(t => ToolTip.SetTip(t, tip));
    }

    private StackPanel Header(AppDefinition d, AppInfo? info)
    {
        var installed = info?.Installed == true;
        var chips = Row(Chip(d.Category),
            installed ? Status("Installed") : Chip("Not found", BorderKey),
            Chip(d.Origin == AppOrigin.Custom ? "Custom app" : "Built-in"));
        var title = new StackPanel
        {
            Spacing = Space.M,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { H1("Apps", "Apps", d.Name), chips }
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.M,
            VerticalAlignment = VerticalAlignment.Top
        };
        if (info?.Folder is { } folder)
        {
            actions.Children.Add(Btn("Open config folder", () => PathUtil.OpenFolder(folder), false, "folder_open"));
        }

        if (d.Logs.Count > 0)
        {
            actions.Children.Add(Btn("View logs", () => x.Go("Logs:" + d.Id), false, "document_text"));
        }

        actions.Children.Add(Btn("Hide", () => AppActions.Hide(x, d), false, "eye_off").With(b => ToolTip.SetTip(b, "Hide this app everywhere. Undo in Settings.")));
        if (d.Origin == AppOrigin.Custom)
        {
            actions.Children.Add(DangerBtn("Remove app", () => AppActions.RemoveApp(x, d), "delete"));
        }

        var head = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        head.Children.Add(Row(AppLogo(d.Logo, d.Category, Sizes.LogoXL), title));

        var sp = new StackPanel
        {
            Spacing = Space.XL,
            Children = { head }
        };
        if (info?.Folder is { } f)
        {
            sp.Children.Add(Row(Txt("Config folder", TextSize.Small, false, Muted), Mono(f)));
        }

        if (d.Notes != null)
        {
            sp.Children.Add(Row(Icon("info", IconSize.S).Themed(TextElement.ForegroundProperty, Muted), Txt(d.Notes, TextSize.Small, false, Muted)));
        }

        return sp;
    }

    /// <summary>Usage card for apps whose session logs have a usage parser; nothing for the others.</summary>
    private Control Usage(AppDefinition d) => UsageCard.Supports(d) ? new UsageCard(x, d, _usage).Build() : new Panel();

    private Border Configs(List<Item> items)
    {
        var cfg = items.Where(i => i.Kind == "Config").OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => Levels.Rank(i.Level)).ToList();
        var rows = cfg.Select(i => new TableRow(
        [
            Txt(i.Name, TextSize.Body, true), LevelCell(i), Txt(i.Description, TextSize.Small), HoldsCell.For(x, i), Mono(i.Path, TextSize.Caption),
            Row(IconBtn("open", "Open file", () => PathUtil.OpenFile(i.Path)), IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(i.Path)), IconBtn("copy", "Copy path", () => Toast.Copy(x.Owner, i.Path)))
        ], () =>
        {
            _preview = _preview == i.Path ? null : i.Path;
            x.Go("App:" + i.App);
        }, _preview == i.Path));
        var body = new StackPanel
        {
            Spacing = Space.L,
            Children = { Table.Build([Cols.Name("File", "1.2*"), Cols.Level(), Cols.Type(), new Col("Holds", "1.6*"), Cols.Path(), Cols.Actions()], rows,
            "No instruction or settings files found. Add project folders to see project files.") }
        };
        if (_preview != null && cfg.Any(i => i.Path == _preview))
        {
            body.Children.Add(new ConfigPanel(x, _preview));
        }

        return Section("document", "Instructions & settings", body);
    }

    private Control Plugins(AppDefinition d, List<Item> items)
    {
        var plugins = items.Where(i => i.Kind == "Plugin").OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => Levels.Rank(i.Level)).ToList();
        if (d.PluginManifest == null && d.Plugins.Count == 0 && plugins.Count == 0)
        {
            return new Panel();
        }

        var manage = d.PluginCommands != null;
        var rows = plugins.Select(p => new TableRow(
        [
            Table.NameDesc(p.Name, p.Description == "" ? p.Path : p.Description, true), LevelCell(p), Txt(p.Version ?? "", TextSize.Small),
            Txt(items.Count(i => i.Plugin == p.Name && i.Kind == "Skill").ToString()),
            Row(IconBtn("copy", "Copy path", () => Toast.Copy(x.Owner, p.Path)), manage ? IconBtn("delete", "Remove plugin", () => AppActions.RemovePlugin(x, d, p)) : new Panel())
        ], () => x.Open(p)));
        var cols = new[] { Cols.Name("Plugin", "3*"), Cols.Level(), Cols.Version(), Cols.Count("Skills"), Cols.Actions() };
        return Section("puzzle_piece", $"Plugins · {plugins.Count}", Table.Build(cols, rows, "No plugins installed."),
            manage ? [Btn("Add plugin", () => AppActions.AddPlugin(x, d), true, "add")] : []);
    }

    /// <summary>The page was left: see <see cref="ItemBrowser.Leave"/>.</summary>
    public void Leave()
    {
        _skillFilter?.Leave();
        _skillFill = () => { };
        _skillSearch = null;
    }

    private Border Skills(AppDefinition d, List<Item> items)
    {
        var filter = _skillFilter ??= new SkillFilter("Skill", null, () => _skillQuery, () => _skillFill(), () =>
        {
            _skillQuery = "";
            _skillSearch?.Text = "";
        });
        if (_skillsApp != d.Id)
        {
            _skillsApp = d.Id;
            filter.Reset();
            _skillQuery = "";
        }
        var skills = items.Where(i => i.Kind == "Skill").OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => Levels.Rank(i.Level)).ToList();
        var index = new ItemSearch(skills, ItemAuthor.For(x.S.Items));
        var query = SearchQuery.Empty;
        TableRow Row(Item s) => new(
        [
            Table.NameDesc(s.Name, s.Description, marks: query), LevelCell(s), InvokeCell(s), Txt(From(s), TextSize.Small, false, Muted),
            W.Row(IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(s.Path)), IconBtn("copy", "Copy path", () => Toast.Copy(x.Owner, s.Path)),
                SkillManager.CannotRemove(s) == null ? IconBtn("delete", "Remove skill", () => AppActions.RemoveSkill(x, s)) : new Panel())
        ], () => x.Open(s));
        var cols = new[] { Cols.Name("Skill", "2.6*"), Cols.Level(), Cols.Type("Invoked by"), new Col("From", ".9*"), Cols.Actions() };
        var body = new ContentControl();
        void Fill()   // re-renders only the table, so the Usage card above is not recalculated and the tool bar keeps its focus and open menu
        {
            if (skills.Count == 0)
            {
                body.Content = Table.Build(cols, [], "No skills found.");
                return;
            }

            query = SearchQuery.Parse(_skillQuery);
            body.Content = Table.Virtual(cols, filter.RowMakers(filter.Apply([.. index.Find(query)], skills), Row, cols.Length, Fill), "No skills match the search or these filters.");
        }
        _skillFill = Fill;
        var search = _skillSearch = SearchBox("Search skills, or author: keyword: name:").With(b => b.Text = _skillQuery)
            .OnPause(q =>
            {
                _skillQuery = q;
                Fill();
            });
        var bar = filter.Bar.Build(search, SearchHelp.Button()).With(b => b.IsVisible = skills.Count > 0);
        Fill();
        var add = AppActions.CanAddSkill(x, d) ? [Btn("Add skill", () => AppActions.AddSkill(x, d), true, "add")] : Array.Empty<Control>();
        return Section("sparkle", $"Skills · {skills.Count}", new StackPanel
        {
            Spacing = Space.XL,
            Children = { bar, body }
        }, add);
    }
}
