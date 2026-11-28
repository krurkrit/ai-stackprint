// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>
/// Gives memory back to the system after something large was closed (a page, the last code view's grammars).
/// Requests are debounced: the collection runs once, two seconds after the last request, so quick navigation never waits on it.
/// </summary>
internal static partial class MemoryTrim
{
    private static DispatcherTimer? _timer;
    private static bool _workingSet;

    /// <param name="workingSet">Also hand the working set back to the system (Windows: the window went to the tray; its pages come back from memory when it opens).</param>
    public static void Request(bool workingSet = false)
    {
        _workingSet |= workingSet;
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
        if (_workingSet && OperatingSystem.IsWindows())
        {
            _ = SetProcessWorkingSetSize(Process(), -1, -1);   // only the window is gone: the system may page the rest out until it is used again
        }

        _workingSet = false;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    private static partial nint Process();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(nint process, nint min, nint max);
}
