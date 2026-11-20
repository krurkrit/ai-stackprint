// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.ExceptionServices;
using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>Starts work nobody awaits (a click handler) without an <c>async void</c> method.</summary>
internal static class Fire
{
    /// <summary>
    /// A failure is raised again on the UI thread, so it reaches the crash handler (crash.log, then the app closes)
    /// exactly as an exception from an async void method did. A cancelled task is ignored.
    /// </summary>
    public static void Forget(Task task) =>
        _ = task.ContinueWith(
            t => Dispatcher.UIThread.Post(() => ExceptionDispatchInfo.Capture(t.Exception!.GetBaseException()).Throw()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
