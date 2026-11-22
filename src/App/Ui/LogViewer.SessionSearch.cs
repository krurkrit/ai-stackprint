// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Conversation and Compact search the whole session file, not only the steps shown: results are places (file and line) the view goes to.</summary>
internal partial class LogViewer
{
    /// <summary>The steps shown are the whole session (nothing older was left out): searching them is the same as searching the file, without reading it again.</summary>
    private bool WholeSessionLoaded => _readable is { } log && _linesOk && !_agentsWindow && log.Session.AgentsBefore == 0 && log.Session.Entries.Count == log.Steps.Count;

    private async Task SearchSessionAsync(LogQuery p)
    {
        if (_file is not { } file)
        {
            return;
        }
#pragma warning disable S2930 // disposed in the finally below when done or replaced
        var cts = _search = new CancellationTokenSource();
#pragma warning restore S2930
        _results.Content = Panel(Busy.Loading("Searching the whole session…", 14));
        try
        {
            var found = await Task.Run(() =>
            {
                var hits = SessionSearch.File(file, p, ResultCap, cts.Token, out var total);
                return (hits, total);
            }, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _hits.AddRange(found.hits);
            (_found, _stepResults) = (found.total, true);
            _jumps.AddRange(found.hits.Select(h => (Action)(() =>
            {
                _pending = h;   // the place (file and line) goes to the view, which finds the step, or reads the steps around it when it is not shown
                GoToPending();
            })));
            _results.Content = Panel(Count(found.total > found.hits.Count ? $"{ReadableLog.Plural(found.total, "step")} (first {found.hits.Count:N0} listed)" : ReadableLog.Plural(found.total, "step")),
                [.. found.hits.Select((h, i) => (Func<Control>)(() => Hit((h.Source != null ? "Sub-agent · " : "") + h.Caption, h.Text, p, i)))]);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _results.Content = Panel(Txt("Cannot read: " + e.Message, TextSize.Caption, false, Muted));
        }
        finally
        {
            if (_search == cts)
            {
                _search = null;
            }

            cts.Dispose();
        }
    }

    private const int ResultCap = 500;   // results listed (the count still says how many steps match)
}
