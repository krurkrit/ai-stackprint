// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>Limit alerts: decided by <see cref="LimitAlerts"/> after each reading and sent through <see cref="Notifier"/>, while the tray is on or the window is open.</summary>
internal sealed partial class AppHost
{
    public static readonly TimeSpan PauseFor = TimeSpan.FromHours(1);
    private readonly LimitAlerts _alerts = new();
    private bool? _registered;

    /// <summary>When paused alerts start again (UTC); null when they are not paused.</summary>
    public DateTime? AlertsPaused => S.State.AlertsPausedUntil is { } u && u > DateTime.UtcNow ? u : null;

    public void PauseAlerts(bool pause)
    {
        S.State.AlertsPausedUntil = pause ? DateTime.UtcNow + PauseFor : null;
        StateStore.Save(S.State);
        _tray?.Update();
    }

    /// <summary>Alerts can be sent: the switch is on and the tray is on, or the window is open.</summary>
    private bool AlertsOn => S.Settings.AlertsEnabled && !_perf && (TrayOn || _main is { InTray: false });

    private void RegisterAlerts()
    {
        Notifier.Message = Message;
        var on = S.Settings.AlertsEnabled && TrayOn;   // the system's entries are written only while the app runs in the tray
        if (!_perf && on != _registered)
        {
            _registered = on;
            Notifier.Current.Register(on);
        }
    }

    private void CheckAlerts(string appId)
    {
        var st = Limits.Status(appId);
        if (st.State != LimitState.Ok || st.Data is not { Windows.Count: > 0 } data || S.Def(appId) is not { } d)
        {
            return;
        }

        var due = _alerts.Check(appId, d.Name, data, Limits.History.Readings(appId), S.Settings, S.State.AlertsSent, S.State.AlertsPausedUntil, DateTime.Now, DateTime.UtcNow, out var changed);
        if (!AlertsOn)
        {
            return;   // what it found is not sent later
        }

        if (changed)
        {
            StateStore.Save(S.State);
        }

        foreach (var a in due)
        {
            Notifier.Current.Show(a);
        }
    }
}
