// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Platform.Storage;

namespace AiStackprint.Ui;

/// <summary>User-confirmed changes from the app detail page. Every action asks first, then rescans.</summary>
internal static class AppActions
{
    /// <summary>Where a new skill can go: (label, level, project). User level first, then each project folder.</summary>
    private static List<(string Label, string Root)> Targets(Ctx x, AppDefinition d)
    {
        var t = new List<(string, string)>();
        if (SkillManager.TargetRoot(d, "User", null) is { } u)
        {
            t.Add(($"User level  ({u})", u));
        }

        foreach (var p in x.S.Projects)
        {
            if (SkillManager.TargetRoot(d, "Project", p) is { } r)
            {
                t.Add(($"Project: {Ctx.ProjectName(p)}  ({r})", r));
            }
        }

        return t;
    }

    public static bool CanAddSkill(Ctx x, AppDefinition d) => Targets(x, d).Count > 0;

    /// <summary>Asks which app first (Skills page, project page), then adds as usual. A project limits targets to that project.</summary>
    public static void AddSkillPickApp(Ctx x, string? project) => Fire.Forget(AddSkillPickAppAsync(x, project));

    private static async Task AddSkillPickAppAsync(Ctx x, string? project)
    {
        var apps = x.S.Defs.Where(d => x.Info(d.Id) != null && (project == null ? CanAddSkill(x, d) : SkillManager.TargetRoot(d, "Project", project) != null))
            .OrderByDescending(d => x.Info(d.Id)!.Installed).ThenBy(d => d.Name).ToList();
        if (apps.Count == 0)
        {
            await Dialogs.Message(x.Owner, "Add skill", "No shown app has a skills folder for this.");
            return;
        }
        var ans = await Dialogs.Ask(x.Owner, "Add skill", "Which app should get the skill?", null, "App", [.. apps.Select(d => d.Name)], "Next");
        if (ans is not { } a)
        {
            return;
        }

        AddSkill(x, apps[a.Choice], project);
    }

    public static void AddPluginPickApp(Ctx x) => Fire.Forget(AddPluginPickAppAsync(x));

    private static async Task AddPluginPickAppAsync(Ctx x)
    {
        var apps = x.S.Defs.Where(d => d.PluginCommands != null && x.Info(d.Id) != null).ToList();
        if (apps.Count == 1)
        {
            AddPlugin(x, apps[0]);
            return;
        }
        var ans = await Dialogs.Ask(x.Owner, "Add plugin", "Which app should get the plugin?", null, "App", [.. apps.Select(d => d.Name)], "Next");
        if (ans is { } a)
        {
            AddPlugin(x, apps[a.Choice]);
        }
    }

    public static void AddSkill(Ctx x, AppDefinition d, string? project = null) => Fire.Forget(AddSkillAsync(x, d, project));

    private static async Task AddSkillAsync(Ctx x, AppDefinition d, string? project = null)
    {
        var picked = await x.Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Skill folder (contains SKILL.md)" });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } src)
        {
            return;
        }

        var targets = Targets(x, d);
        if (project != null && SkillManager.TargetRoot(d, "Project", project) is { } pr)
        {
            targets = [($"Project: {Ctx.ProjectName(project)}  ({pr})", pr)];
        }

        var ans = await Dialogs.Ask(x.Owner, $"Add skill to {d.Name}", $"Copies \"{Path.GetFileName(src.TrimEnd('/', '\\'))}\" into the chosen skills folder. The original stays where it is.",
            null, "Install to", [.. targets.Select(t => t.Label)], "Add skill");
        if (ans is not { } a)
        {
            return;
        }

        await Try(x, "Add skill", () =>
        {
            var dest = SkillManager.Install(src, targets[a.Choice].Root);
            return $"Added to {dest}";
        });
    }

    public static void RemoveSkill(Ctx x, Item skill) => Fire.Forget(RemoveSkillAsync(x, skill));

    private static async Task RemoveSkillAsync(Ctx x, Item skill)
    {
        if (SkillManager.CannotRemove(skill) is { } why)
        {
            await Dialogs.Message(x.Owner, "Cannot remove skill", why);
            return;
        }
        if (!await Dialogs.Confirm(x.Owner, $"Remove {skill.Name}?",
                $"The skill is moved out of {skill.Source} into a backup folder you can restore from:\n{SkillManager.BackupRoot}", "Remove", true))
        {
            return;
        }

        await Try(x, "Remove skill", () => $"Moved to {SkillManager.Remove(skill)}");
    }

    public static void AddPlugin(Ctx x, AppDefinition d) => Fire.Forget(AddPluginAsync(x, d));

    private static async Task AddPluginAsync(Ctx x, AppDefinition d)
    {
        var scopes = new List<(string Label, string Scope, string? Project)> { ("User (all projects)", "user", null) };
        foreach (var p in x.S.Projects)
        {
            scopes.Add(($"Project: {Ctx.ProjectName(p)} (shared with the repo)", "project", p));
        }

        var ans = await Dialogs.Ask(x.Owner, $"Add plugin to {d.Name}", $"Runs the app's own CLI: {string.Join(' ', d.PluginCommands!.Install)}",
            "name@marketplace", "Scope", [.. scopes.Select(s => s.Label)], "Install");
        if (ans is not { } a)
        {
            return;
        }

        var (_, scope, project) = scopes[a.Choice];
        await Run(x, "Install plugin", () => PluginManager.Install(d, a.Text, scope, project));
    }

    public static void RemovePlugin(Ctx x, AppDefinition d, Item plugin) => Fire.Forget(RemovePluginAsync(x, d, plugin));

    private static async Task RemovePluginAsync(Ctx x, AppDefinition d, Item plugin)
    {
        List<string> cmd;
        try { cmd = PluginManager.Build(d.PluginCommands!.Remove, plugin.Name, plugin.Scope ?? "user"); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            await Dialogs.Message(x.Owner, "Cannot remove plugin", e.Message);
            return;
        }
        var where = plugin.Project != null ? $" in {plugin.Project}" : "";
        if (!await Dialogs.Confirm(x.Owner, $"Remove {plugin.Name}?", $"Runs{where}:\n{string.Join(' ', cmd)}", "Remove", true))
        {
            return;
        }

        await Run(x, "Remove plugin", () => PluginManager.Remove(d, plugin));
    }

    public static void RemoveApp(Ctx x, AppDefinition d) => Fire.Forget(RemoveAppAsync(x, d));

    private static async Task RemoveAppAsync(Ctx x, AppDefinition d)
    {
        var builtIn = d.Origin == AppOrigin.Custom && x.S.Def(d.Id) != d;   // your entry for a tool that is now built in
        if (!await Dialogs.Confirm(x.Owner, $"Remove {d.Name}?",
                builtIn ? $"{d.Name} is now built in, so your entry in apps.json is not used. Removing it deletes only that entry." : "Deletes the app from apps.json. Its files on disk are not touched.",
                "Remove", true))
        {
            return;
        }

        await Try(x, "Remove app", () =>
        {
            AppCatalog.RemoveUser(d.Id);
            x.S.Use(AppCatalog.Read());
            return "";
        }, back: !builtIn);
    }

    public static void Hide(Ctx x, AppDefinition d)
    {
        if (!x.S.Settings.HiddenApps.Contains(d.Id))
        {
            x.S.Settings.HiddenApps.Add(d.Id);
        }

        SettingsStore.Save(x.S.Settings);
        x.Go("Apps");
        x.Rescan();
    }

    private static async Task Try(Ctx x, string title, Func<string> act, bool back = false)
    {
        try
        {
            var msg = act();
            if (msg != "")
            {
                await Dialogs.Message(x.Owner, title, msg);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.Message(x.Owner, title + " failed", e.Message);
        }
        if (back)
        {
            x.Go("Apps");
        }

        x.Rescan();
    }

    private static async Task Run(Ctx x, string title, Func<Task<(int Code, string Output)>> act)
    {
        try
        {
            var (code, output) = await act();
            await Dialogs.Message(x.Owner, code == 0 ? title : $"{title} failed (exit {code})", output, mono: true);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            await Dialogs.Message(x.Owner, title + " failed", e.Message);
        }
        x.Rescan();
    }
}
