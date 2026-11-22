// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>A long session is read cut to its end, so its older sub-agents are not shown: the summary offers to load the part around the newest of them, and to go back.</summary>
internal partial class LogViewer
{
    private DateTime? _agentsFrom;    // set: show the part of the file around the newest sub-agent that started before this time
    private string? _aroundAgent;     // set: show the part of the session around this sub-agent file (a search result in it)
    private long _aroundLine;         // set: show the steps around this file line (a search result the steps shown do not hold)
    private bool _keepResults;         // the next load was asked for by a click on a search result: the results are not searched again
    private bool _agentsWindow;       // the part shown is such a window (around sub-agents or a result), not the end of the file

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
        _aroundLine = 0;
        _aroundAgent = null;
        Follow = false;
        _keepResults = true;
        LoadAsync(false);
    }

    /// <summary>Loads the part of the session around a sub-agent (a search result in its file).</summary>
    private void ShowAgent(string agentFile)
    {
        _aroundAgent = agentFile;
        _aroundLine = 0;
        _agentsFrom = null;
        Follow = false;
        _keepResults = true;
        LoadAsync(false);
    }

    /// <summary>Loads the steps around a file line: a search result in the part of a long file that is not shown.</summary>
    private void ShowLine(long line)
    {
        _aroundLine = line;
        _aroundAgent = null;
        _agentsFrom = null;
        Follow = false;
        _keepResults = true;
        LoadAsync(false);
    }

    private void ShowEnd()
    {
        _agentsFrom = null;
        _aroundLine = 0;
        _aroundAgent = null;
        _agentsWindow = false;
        _keepResults = true;
        LoadAsync(true);
    }
}
