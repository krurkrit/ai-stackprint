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
    private readonly SkillFilter _skillFilter = new();
    private string? _skillsApp;   // app the skill filters belong to: reset when another app opens

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
            Chip(d.Origin switch { AppOrigin.Custom => "Custom app", AppOrigin.Override => "Customised in apps.json", _ => "Built-in" }));
        var title = new StackPanel { Spacing = Space.M, VerticalAlignment = VerticalAlignment.Center, Children = { H1("Apps", "Apps", d.Name), chips } };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.M, VerticalAlignment = VerticalAlignment.Top };
        if (info?.Folder is { } folder)
        {
            actions.Children.Add(Btn("Open config folder", () => PathUtil.OpenFolder(folder), false, "folder_open"));
        }

        if (d.Logs.Count > 0)
        {
            actions.Children.Add(Btn("View logs", () => x.Go("Logs:" + d.Id), false, "document_text"));
        }

        actions.Children.Add(Btn("Hide", () => AppActions.Hide(x, d), false, "eye_off").With(b => ToolTip.SetTip(b, "Hide this app everywhere. Undo in Settings.")));
        if (d.Origin != AppOrigin.BuiltIn)
        {
            actions.Children.Add(DangerBtn(d.Origin == AppOrigin.Custom ? "Remove app" : "Reset to built-in", () => AppActions.RemoveApp(x, d), "delete"));
        }

        var head = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        head.Children.Add(Row(AppLogo(d.Logo, d.Category, Sizes.LogoXL), title));

        var sp = new StackPanel { Spacing = Space.XL, Children = { head } };
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
        var cfg = items.Where(i => i.Kind == "Config").OrderBy(i => i.Description).ThenBy(i => Levels.Rank(i.Level)).ToList();
        var rows = cfg.Select(i => new TableRow(
        [
            Txt(i.Name, TextSize.Body, true), Txt(i.Description, TextSize.Small), LevelCell(i), Mono(i.Path, TextSize.Caption),
            Row(IconBtn("open", "Open file", () => PathUtil.OpenFile(i.Path)), IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(i.Path)), IconBtn("copy", "Copy path", () => Toast.Copy(x.Owner, i.Path)))
        ], () => { _preview = _preview == i.Path ? null : i.Path; x.Go("App:" + i.App); }, _preview == i.Path));
        var body = new StackPanel
        {
            Spacing = Space.L,
            Children = { Table.Build([new Col("File", "1.2*"), new Col("Type", "1*"), new Col("Level", "1.2*"), new Col("Path", "3*"), new Col("", "112")], rows,
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
        var plugins = items.Where(i => i.Kind == "Plugin").OrderBy(i => i.Name).ToList();
        if (d.PluginManifest == null && plugins.Count == 0)
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
        var cols = new[] { new Col("Plugin", "3*"), new Col("Level", "1.2*"), new Col("Version", "1*"), new Col("Skills", ".6*", true), new Col("", "80") };
        return Section("puzzle_piece", $"Plugins · {plugins.Count}", Table.Build(cols, rows, "No plugins installed."),
            manage ? [Btn("Add plugin", () => AppActions.AddPlugin(x, d), true, "add")] : []);
    }

    private Border Skills(AppDefinition d, List<Item> items)
    {
        if (_skillsApp != d.Id) { _skillsApp = d.Id; _skillFilter.Reset(); }
        var skills = items.Where(i => i.Kind == "Skill").OrderBy(i => Levels.Rank(i.Level)).ThenBy(i => i.Name).ToList();
        TableRow Row(Item s) => new(
        [
            Table.NameDesc(s.Name, s.Description), LevelCell(s), InvokeCell(s), Txt(From(s), TextSize.Small, false, Muted),
            W.Row(IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(s.Path)), IconBtn("copy", "Copy path", () => Toast.Copy(x.Owner, s.Path)),
                SkillManager.CannotRemove(s) == null ? IconBtn("delete", "Remove skill", () => AppActions.RemoveSkill(x, s)) : new Panel())
        ], () => x.Open(s));
        var cols = new[] { new Col("Skill", "2.6*"), new Col("Level", "1.3*"), new Col("Invoked by", "1*"), new Col("From", ".9*"), new Col("", "112") };
        var body = new ContentControl();
        void Fill()   // re-renders only this section, so the Usage card above is not recalculated
        {
            if (skills.Count == 0)
            {
                body.Content = Table.Build(cols, [], "No skills found.");
                return;
            }

            var filters = _skillFilter.Controls(skills, skill: true, Fill);   // first: drops filter values no longer offered, before rows are matched
            var table = Table.Build(cols, _skillFilter.Rows([.. skills.Where(_skillFilter.Match)], Row, cols.Length, Fill), "No skills match these filters.");
            body.Content = new StackPanel { Spacing = Space.XL, Children = { filters, table } };
        }
        Fill();
        var add = AppActions.CanAddSkill(x, d) ? [Btn("Add skill", () => AppActions.AddSkill(x, d), true, "add")] : Array.Empty<Control>();
        return Section("sparkle", $"Skills · {skills.Count}", body, add);
    }
}
