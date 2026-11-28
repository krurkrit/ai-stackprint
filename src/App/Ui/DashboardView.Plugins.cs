// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class DashboardView
{
    private const int TopKeywords = 12;

    /// <summary>
    /// What plugins add (skills, commands, agents, hooks, MCP and LSP servers: <see cref="PluginParts"/>), each bar out of all plugins,
    /// and the plugins' most used keywords. A bar or keyword opens the Plugins list searched for it (has:mcp, keyword:"x").
    /// </summary>
    private static Control PluginContents(Ctx x)
    {
        var plugins = x.S.Items.Where(i => i.Kind == "Plugin").ToList();
        if (plugins.Count == 0)
        {
            return Txt("No plugins found.", TextSize.Small, false, Muted);
        }

        var sp = new StackPanel { Spacing = Space.M };
        foreach (var p in PluginParts.All)
        {
            var n = plugins.Count(i => i.Provides.Contains(p.Key));
            if (n > 0)
            {
                sp.Children.Add(Bar(Txt(p.Label, TextSize.Small, true), [(n, ChartKey(1))], n, plugins.Count, () => x.Browse("Plugin", null, "has:" + p.Key),
                    $"{n} of {plugins.Count} plugins add {p.Label.ToLowerInvariant()}"));
            }
        }

        if (sp.Children.Count == 0)
        {
            sp.Children.Add(Txt("No plugin adds skills, commands, agents, hooks or servers that could be found.", TextSize.Small, false, Muted));
        }

        var keywords = plugins.SelectMany(i => i.Keywords).GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Take(TopKeywords).ToList();
        if (keywords.Count > 0)
        {
            var wrap = new WrapPanel
            {
                ItemSpacing = Space.S,
                LineSpacing = Space.S
            };
            foreach (var g in keywords)
            {
                wrap.Children.Add(Keyword(g.Key, g.Count(), () => x.Browse("Plugin", null, $"keyword:\"{g.Key}\"")));
            }

            sp.Children.Add(new StackPanel
            {
                Spacing = Space.S,
                Margin = new Avalonia.Thickness(0, Space.M, 0, 0),
                Children = { Txt("Top keywords", TextSize.Caption, true, Muted), wrap }
            });
        }

        return sp;
    }

    /// <summary>A keyword chip with how many plugins use it; a click opens the plugins that do.</summary>
    private static Border Keyword(string word, int n, Action open)
    {
        var chip = (Border)Chip(n > 1 ? $"{word} · {n}" : word);
        chip.Cursor = Hand;
        ToolTip.SetTip(chip, $"{n} {(n == 1 ? "plugin" : "plugins")}: show {(n == 1 ? "it" : "them")}");
        chip.Hover(SelectionKey, Subtle);
        chip.PointerReleased += (_, _) => open();
        return chip;
    }
}
