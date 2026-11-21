// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>A conversation with sub-agents: each step has its row of the branch graph on its right.</summary>
internal partial class ReadableLog
{
    /// <summary>The step and its graph cell. The cell also covers the space below the step, so the lines do not break between steps.</summary>
    private Panel GraphRow(Control step, int index, LogEntry e)
    {
        var graph = _graph!;
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"*,{Space.XL},Auto"),   // the step, a gutter, the graph column
            RowDefinitions = new RowDefinitions($"Auto,{Space.M}")
        };
        row.Children.Add(step);
        var tip = graph.Rows[index].Owner.Agent is > 0 and var agent ? graph.Agents[agent] : null;
        var cell = new AgentGraphCell(graph.Rows[index], graph.Lanes, e.Role, tip);
        // The graph is a column of its own: the side menu's background and a line on its left, unbroken from the first step to the last
        var column = new Border
        {
            Child = cell,
            BorderThickness = new Thickness(Stroke.Thin, 0, 0, 0)
        }.Themed(Border.BackgroundProperty, SideKey).Themed(Border.BorderBrushProperty, BorderKey);
        Grid.SetColumn(column, 2);
        Grid.SetRowSpan(column, 2);
        row.Children.Add(column);
        return row;
    }

    /// <summary>The agents above the steps: Main and each sub-agent with the color of its branch.</summary>
    private static WrapPanel AgentLegend(SessionGraph graph)
    {
        var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, Space.M) };
        for (var i = 0; i < graph.Agents.Length; i++)
        {
            var key = i == 0 ? Muted : CategoryKey(Theme.Categories[(i - 1) % Theme.Categories.Length]);
            legend.Children.Add(Chip(graph.Agents[i], key).With(c => c.Margin = new Thickness(0, 0, Space.M, Space.S)));
        }

        return legend;
    }
}
