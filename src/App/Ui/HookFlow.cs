// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The hook events of one app as a lifecycle diagram (after the Claude Code docs), compact enough to show without scrolling: the main order on the left,
/// the events outside it on the right. A picker at the top chooses the app. An event the user has a hook for (in the Event filter list) carries a label with how many.
/// Hover a step for what happens there.
/// </summary>
internal sealed partial class HookFlow : StackPanel
{
    private readonly IReadOnlyList<Hook> _hooks;
    private readonly ContentControl _body = new();
    private Dictionary<string, int> _mine = [];

    /// <param name="hooks">Every hook on the Hooks page: the diagram labels the events of the chosen app that appear.</param>
    public HookFlow(IReadOnlyList<Hook> hooks)
    {
        _hooks = hooks;
        Spacing = Space.S;
        // the app with the most hooks first, so the diagram opens on the one the user uses
        var first = HookFlows.All.OrderByDescending(f => hooks.Count(h => h.Apps.Intersect(f.AppIds).Any())).First();
        Children.Add(Panel(first));
    }

    private Control Panel(HookFlowDef current)
    {
        var host = new StackPanel { Spacing = Space.S };
        host.Children.Add(Segmented(HookFlows.All.Select(f => (f.Label, f.Label)), current.Label, v => Show(HookFlows.All.First(f => f.Label == v), host)));
        host.Children.Add(_body);
        Show(current, host);
        return host;
    }

    private void Show(HookFlowDef flow, StackPanel host)
    {
        _mine = _hooks.Where(h => h.Apps.Intersect(flow.AppIds).Any()).GroupBy(h => h.Event, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        host.Children[0] = Segmented(HookFlows.All.Select(f => (f.Label, f.Label)), flow.Label, v => Show(HookFlows.All.First(f => f.Label == v), host));

        var body = new StackPanel { Spacing = Space.S };
        body.Children.Add(Txt(_mine.Count == 0 ? $"None of these events is in your {flow.Label} hooks yet." : "A blue number: how many of your hooks use that event. Hover a step to see what happens there.", TextSize.Caption, false, Muted));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(flow.Other.Length > 0 ? "*,Auto" : "*"), ColumnSpacing = Space.L };
        grid.Children.Add(Nodes(flow.Main, true));
        if (flow.Other.Length > 0)
        {
            grid.Children.Add(AnyTime(flow.Other).With(a => Grid.SetColumn(a, 1)));
        }

        body.Children.Add(grid);
        if (flow.Note != null)
        {
            body.Children.Add(Txt(flow.Note, TextSize.Caption, false, Muted));
        }

        _body.Content = body;
    }
}
