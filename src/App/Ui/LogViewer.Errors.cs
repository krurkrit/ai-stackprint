// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>Lines view of a text log: go to the next or previous error line (F8 / Shift+F8 or the buttons).</summary>
internal partial class LogViewer
{
    private LogEntry? _errorAt;   // the error the last jump went to

    /// <summary>Scrolls to the next (1) or previous (-1) error after the first line in view; false when the view has no errors in that direction.</summary>
    public bool NextError(int step)
    {
        if (_readable is not { } log || !ShowReadable || !IsLines)
        {
            return false;
        }

        var steps = log.Steps;
        var top = log.StepAtTop();
        var from = top == null ? (step > 0 ? -1 : steps.Count) : steps.IndexOf(top);
        if (_errorAt != null && steps.IndexOf(_errorAt) is var last && last >= 0 && Math.Abs(last - from) < 15)
        {
            from = last;   // the view could not put the error in the middle (near an end): go on from the error itself
        }

        for (var i = from + step; i >= 0 && i < steps.Count; i += step)
        {
            if (steps[i].Role == "error")
            {
                _errorAt = steps[i];
                log.Reveal(steps[i]);
                var errors = steps.Where(e => e.Role == "error").ToList();
                _status.Text = $"Error {errors.IndexOf(steps[i]) + 1:N0} of {errors.Count:N0}";
                return true;
            }
        }

        _status.Text = steps.Any(e => e.Role == "error") ? (step > 0 ? "No more errors below" : "No more errors above") : "No errors in this file";
        return false;
    }
}
