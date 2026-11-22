// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Skills, Plugins or Commands &amp; agents page (kind "Part": Command and Agent items, standalone or from a plugin): the full list with filters and
/// the bottom detail panel. One instance per page, so each keeps its own state.
/// </summary>
internal class LibraryView(Ctx x, string kind)
{
    private readonly ItemBrowser _browser = new(x, kind);

    public void Leave() => _browser.Leave();
    public void Select(Item i) => _browser.Select(i);
    public void Filter(string? app, string? query) => _browser.Filter(app, query);

    public static bool Holds(string kind, Item i) => kind == "Part" ? i.Kind is "Command" or "Agent" : i.Kind == kind;

    public Control Build()
    {
        var head = Head();
        var all = x.S.Items.Where(i => Holds(kind, i)).ToList();
        Control? right = kind switch
        {
            "Skill" => Btn("Add skill", () => AppActions.AddSkillPickApp(x, null), true, "add"),
            "Plugin" when x.S.Defs.Any(d => d.PluginCommands != null) => Btn("Add plugin", () => AppActions.AddPluginPickApp(x), true, "add"),
            _ => null   // commands and agents: the Type filter in the tool bar
        };
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }
        head.Children.Add(H1(kind switch { "Plugin" => "Plugins", "Part" => "Commands & Agents", _ => "Skills" }));
        return _browser.Build([head], all);
    }
}
