// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Skills or Plugins page: the full list with filters and the bottom detail panel. One instance per page, so each keeps its own state.</summary>
internal class LibraryView(Ctx x, string kind)
{
    private readonly ItemBrowser _browser = new(x, kind);

    public void Leave() => _browser.Leave();
    public void Select(Item i) => _browser.Select(i);
    public void Filter(string? app, string? query) => _browser.Filter(app, query);

    public Control Build()
    {
        var head = new DockPanel();
        Control? add = kind == "Skill" ? Btn("Add skill", () => AppActions.AddSkillPickApp(x, null), true, "add")
            : x.S.Defs.Any(d => d.PluginCommands != null) ? Btn("Add plugin", () => AppActions.AddPluginPickApp(x), true, "add") : null;
        if (add != null) { DockPanel.SetDock(add, Dock.Right); head.Children.Add(add); }
        head.Children.Add(H1(kind == "Plugin" ? "Plugins" : "Skills"));
        return _browser.Build([head], [.. x.S.Items.Where(i => i.Kind == kind)]);
    }
}
