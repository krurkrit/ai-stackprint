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
            if (_readable is { } log && (log.Steps.LastOrDefault(e => e.Line is > 0 && e.Line <= line) ?? log.Steps.FirstOrDefault()) is { } step)
            {
                log.Reveal(step);
            }
        }
        else
        {
            _pending = new SearchHit(line, "", 0, 0);   // the raw view goes to a line the same way it goes to a search result
            GoToPending();
        }
    }
}
