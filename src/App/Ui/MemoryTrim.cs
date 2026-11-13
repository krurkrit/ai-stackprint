using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>
/// Gives memory back to the system after something large was closed (a page, the last code view's grammars).
/// Requests are debounced: the collection runs once, two seconds after the last request, so quick navigation never waits on it.
/// </summary>
static class MemoryTrim
{
    static DispatcherTimer? _timer;

    public static void Request()
    {
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => { _timer!.Stop(); Compact(); });
        _timer.Stop(); _timer.Start();
    }

    static void Compact()
    {
        GC.Collect(); GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
