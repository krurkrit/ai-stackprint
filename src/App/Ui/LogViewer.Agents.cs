// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>A long session is read cut to its end, so its older sub-agents are not shown: the summary offers to load the part around the newest of them, and to go back.</summary>
internal partial class LogViewer
{
    private DateTime? _agentsFrom;    // set: show the part of the file around the newest sub-agent that started before this time
    private bool _agentsWindow;       // the part shown is such a window, not the end of the file

    /// <summary>The summary's links: earlier sub-agents (when some are left) and, inside a window, back to the end. Null for a file without either.</summary>
    private AgentsNav? NavFor(Session s)
    {
        Action? earlier = s.AgentsBefore > 0 && s.Entries.FirstOrDefault(e => e.Time != null)?.Time is { } first
            ? () => ShowAgentsBefore(first)
            : null;
        Action? back = _agentsWindow ? ShowEnd : null;
        return earlier == null && back == null ? null : new AgentsNav(earlier, back);
    }

    private void ShowAgentsBefore(DateTime before)
    {
        _agentsFrom = before;
        Follow = false;
        LoadAsync(false);
    }

    private void ShowEnd()
    {
        _agentsFrom = null;
        _agentsWindow = false;
        LoadAsync(true);
    }
}
