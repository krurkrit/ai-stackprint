// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;

namespace AiStackprint.Ui;

/// <summary>
/// With the tray on (Settings › Usage limits), × hides the window instead of quitting: the page lets go of its controls and memory is given back,
/// while the tray, limit checks, widget and alerts go on. Opening it again shows the page it was left on.
/// </summary>
internal partial class MainWindow
{
    /// <summary>True while the window is hidden in the tray.</summary>
    public bool InTray { get; private set; }

    private void Tray()
    {
        Closing += (_, e) =>
        {
            if (_host is { } h && e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic && h.KeepsRunning)
            {
                e.Cancel = true;
                ToTray();
                h.WentToTray();
            }
        };
        Closed += (_, _) => _host?.MainClosed();
    }

    /// <summary>Hides the window and drops its page (built again on <see cref="FromTray"/>).</summary>
    public void ToTray()
    {
        InTray = true;
        _palette.Close();
        if (_scanned)
        {
            Leave(_shownView);
            if (_wasLogs)
            {
                _logs.Release();
                _wasLogs = false;
            }

            _shownView = "";
            _x.Escape = null;
            _body.Content = null;
        }

        Hide();
        MemoryTrim.Request(workingSet: true);
    }

    /// <summary>Shows the window again on the page it was left on, or on <paramref name="view"/>.</summary>
    public void FromTray(string? view)
    {
        var hidden = InTray;
        InTray = false;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        if (view != null && _scanned)
        {
            Go(view);
        }
        else if (view != null)
        {
            _view = view;   // the first scan shows it when done
        }
        else if (hidden)
        {
            Refresh();
        }
    }
}
