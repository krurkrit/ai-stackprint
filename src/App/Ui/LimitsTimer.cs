// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>
/// Every 20 seconds asks <see cref="LimitsService"/> for the limits of the apps it is given; the service decides whether a call is due
/// (refresh interval, at most once a minute). It runs with or without the main window: started by <see cref="AppHost"/>, or by a main window of its own in tests.
/// </summary>
internal sealed class LimitsTimer
{
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);
    private readonly LimitsService _limits;
    private readonly Func<IEnumerable<AppDefinition>> _apps;
    private readonly Func<IReadOnlyList<string>> _projects;
    private readonly Func<bool> _paused;
    private readonly DispatcherTimer _timer;

    public LimitsTimer(LimitsService limits, Func<IEnumerable<AppDefinition>> apps, Func<IReadOnlyList<string>> projects, Func<bool> paused)
    {
        (_limits, _apps, _projects, _paused) = (limits, apps, projects, paused);
        _limits.Updated += id => Dispatcher.UIThread.Post(() => Changed?.Invoke(id));
        _timer = new DispatcherTimer(Tick, DispatcherPriority.Background, (_, _) => Request());
        _timer.Start();
    }

    /// <summary>Raised on the UI thread with the app id when its limits change.</summary>
    public event Action<string>? Changed;

    public void Request()
    {
        if (_paused())
        {
            return;
        }

        foreach (var d in _apps())
        {
            _limits.Request(d, _projects());
        }
    }

    public void Stop() => _timer.Stop();

    /// <summary>
    /// The apps whose limits are asked for: after a scan, every installed app with a provider (Codex limits also come from its log, with no call);
    /// before one (started in the tray), the apps whose live limits are on.
    /// </summary>
    public static IEnumerable<AppDefinition> Apps(AppState s, Func<string, bool> enabled) =>
        s.Defs.Where(d => d.Limits != null && LimitProviders.Find(d.Limits) != null
            && (s.Apps.Count > 0 ? s.Apps.Any(a => a.Id == d.Id && a.Installed) : enabled(d.Id)));
}
