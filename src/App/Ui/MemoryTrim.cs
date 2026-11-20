// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>
/// Gives memory back to the system after something large was closed (a page, the last code view's grammars).
/// Requests are debounced: the collection runs once, two seconds after the last request, so quick navigation never waits on it.
/// </summary>
internal static class MemoryTrim
{
    private static DispatcherTimer? _timer;

    public static void Request()
    {
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
        {
            _timer!.Stop();
            Compact();
        });
        _timer.Stop();
        _timer.Start();
    }

    private static void Compact()
    {
#pragma warning disable S1215 // the point of this class: give memory back after a large page closes (debounced, at most every 2 s)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
#pragma warning restore S1215
    }
}
