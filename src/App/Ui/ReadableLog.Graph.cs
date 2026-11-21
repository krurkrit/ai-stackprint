// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

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
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions($"Auto,{Space.M}")
        };
        row.Children.Add(step);
        var tip = graph.Rows[index].Owner.Agent is > 0 and var agent ? graph.Agents[agent] : null;
        var cell = new AgentGraphCell(graph.Rows[index], graph.Lanes, e.Role, tip);
        Grid.SetColumn(cell, 1);
        Grid.SetRowSpan(cell, 2);
        row.Children.Add(cell);
        return row;
    }
}
