// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Each step has its row of the graph on its right: the main line with a dot per step (red for an error, blue for you, ringed when bookmarked), and the branches of sub-agents.</summary>
internal partial class ReadableLog
{
    /// <summary>The step and its graph cell. The column also covers the space below a card, so the lines do not break between steps.</summary>
    private Panel GraphRow(Control step, int index, LogEntry e)
    {
        var graph = _graph!;
        var row = new Grid
        {
            RowDefinitions = new RowDefinitions(_compact ? "Auto" : $"Auto,{Space.M}")   // cards carry the space below them, so the lines run on
        };
        row.Children.Add(step);
        _showStep[e] = on =>
        {
            step.IsVisible = on;   // the graph stays, so the lines run on over the hidden steps
            if (!_compact)
            {
                row.RowDefinitions[1].Height = new GridLength(on ? Space.M : 0);
            }
        };
        var cell = new AgentGraphCell(graph.Rows[index], graph.Lanes, e.IsError ? "error" : e.Role, null, _compact ? Heights.LogRow / 2 : Sizes.GraphNodeY, () => _marks?.Get(e) != null);
        _marks?.Watch(e, cell.InvalidateVisual);
        // The graph lies over the right edge of the steps (it does not take a column of its own) and passes clicks through; the lines run unbroken from the first step to the last
        cell.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        cell.IsHitTestVisible = false;
        Grid.SetRowSpan(cell, _compact ? 1 : 2);
        row.Children.Add(cell);
        return row;
    }

    private readonly Dictionary<LogEntry, Action<bool>> _showStep = [];
    private readonly HashSet<int> _hiddenAgents = [];   // agents (0 = main) the legend chips turned off

    /// <summary>The agents above the steps: a chip for Main and each sub-agent in the color of its branch; click one to hide or show its steps.</summary>
    private WrapPanel AgentLegend(SessionGraph graph)
    {
        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, Space.M) };
        for (var i = 0; i < graph.Agents.Length; i++)
        {
            var agent = i;
            var key = i == 0 ? Muted : CategoryKey(Theme.Categories[(i - 1) % Theme.Categories.Length]);
            var chip = new FilterChip(WithDot(Dot(key), graph.Agents[i], TextSize.Caption, true), "Show or hide this agent's steps", on =>
            {
                if (on)
                {
                    _hiddenAgents.Remove(agent);
                }
                else
                {
                    _hiddenAgents.Add(agent);
                }

                for (var n = 0; n < Steps.Count; n++)
                {
                    if (_showStep.TryGetValue(Steps[n], out var show))
                    {
                        show(!_hiddenAgents.Contains(graph.Rows[n].Owner.Agent));
                    }
                }
            });
            legend.Children.Add(chip);
        }

        return legend;
    }
}
