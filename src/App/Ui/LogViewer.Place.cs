// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>Conversation and Raw show the same file: switching keeps the place. The step (or line) in the middle of one view is put in the middle of the other.</summary>
internal partial class LogViewer
{
    private long _place;       // file line to show in the middle after the next load; 0 = none
    private bool _linesOk;     // the steps' line numbers are the file's (the file was read whole)

    /// <summary>Called before the view switches: notes the file line in the middle of the view being left.</summary>
    private void RememberPlace()
    {
        _place = 0;
        if (!_linesOk || _only.IsChecked == true)
        {
            return;
        }

        if (ShowReadable)
        {
            _place = _readable?.StepAtCenter()?.Line ?? 0;
        }
        else if (_code?.CenterLine() is > 0 and var line)
        {
            _place = _firstLine + line - 1;
        }
    }

    /// <summary>After the other view has loaded: puts the remembered place in its middle.</summary>
    private void ShowRememberedPlace()
    {
        var line = _place;
        _place = 0;
        if (line <= 0 || _file == null)
        {
            return;
        }

        if (ShowReadable)
        {
            RevealLine(line, 0);
        }
        else
        {
            _pending = new SearchHit(line, "", 0, 0);   // the raw view goes to a line the same way it goes to a search result
            GoToPending();
        }
    }

    /// <summary>Goes to a step the app already has (a search result among the steps, an error): by its place in its file, like every other way to a step.</summary>
    private void RevealStep(LogEntry e)
    {
        if (e.Line > 0 && _readable?.RevealAt(e.Source, e.Line, FadeSeconds) == true)
        {
            return;
        }

        _readable?.Reveal(e, FadeSeconds);
    }

    /// <summary>Goes to the step that holds this line of the session file: the last step that starts at or before it.</summary>
    private void RevealLine(long line, double fade) => RevealAt(null, line, fade);

    /// <summary>Goes to the step that holds a place in the session file (<paramref name="source"/> null) or in a sub-agent's file; the first step when the place is older than the steps shown.</summary>
    private void RevealAt(string? source, long line, double fade)
    {
        if (_readable is { } log && !log.RevealAt(source, line, fade) && log.Steps.FirstOrDefault() is { } first)
        {
            log.Reveal(first, fade);
        }
    }
}
