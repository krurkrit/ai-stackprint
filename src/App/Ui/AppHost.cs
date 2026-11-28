// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AiStackprint.Ui;

/// <summary>
/// The running app, in one process: settings, projects and live limits (with their <see cref="LimitsTimer"/>), the main window (made when first opened),
/// and, while "Keep running in the tray" is on, the tray icon, the usage widget and alerts with the window closed.
/// Started with <c>--tray</c> (the login entry) it shows no window and skips the scan until the window is first opened.
/// </summary>
internal sealed partial class AppHost
{
    private readonly IAppLifetime _life;
    private readonly bool _perf, _atLogin;
    private readonly CancellationTokenSource _stop = new();
    private MainWindow? _main;
    private TrayHost? _tray;
    private UsageWidget? _widget;
    private bool? _loginOn;

    public AppState S { get; } = new();
    public LimitsService Limits { get; }
    public LimitsTimer Timer { get; }
    public bool Quitting { get; private set; }

    /// <summary>The pipe a second start talks to; tests give each host its own, so they never meet a running copy of the app.</summary>
    internal string Pipe { get; set; } = SingleInstance.PipeName;

    /// <summary>The main window, once it was opened.</summary>
    public MainWindow? Main => _main;
    public TrayHost? Tray => _tray;
    public UsageWidget? Widget => _widget;

    public AppHost(IAppLifetime life, IReadOnlyList<string> args)
    {
        _life = life;
        _perf = args.Contains("--perf");
        _atLogin = args.Contains("--tray");
        S.Settings = SettingsStore.Load();   // first: it hands values older versions kept there to state.json
        S.State = StateStore.Load();
        MainWindow.LoadProjects(S);
        S.Use(AppCatalog.Read());
        Theme.Appearance(Application.Current!, S.Settings);
        Limits = new LimitsService(() => S.Settings);
        Timer = new LimitsTimer(Limits, () => LimitsTimer.Apps(S, Limits.Enabled), () => S.Projects, Paused);
        Timer.Changed += Changed;
    }

    /// <summary>The tray is on: the app keeps running with no window.</summary>
    public bool TrayOn => S.Settings.TrayEnabled && !_perf;

    /// <summary>× on the main window hides it instead of quitting.</summary>
    public bool KeepsRunning => TrayOn && S.Settings.CloseToTray && !Quitting;

    /// <summary>Limit checks wait while nothing would show them: the tray is off and the window is minimized or not open.</summary>
    private bool Paused() => !TrayOn && (_main == null || _main.InTray || _main.WindowState == WindowState.Minimized);

    public void Start(IReadOnlyList<string> args)
    {
        if (!_perf)
        {
            var ui = Dispatcher.UIThread;   // messages come on a worker thread: see LimitsTimer
            _ = SingleInstance.Listen(Pipe, m => ui.Post(() => Message(m)), _stop.Token);
        }

        Apply();
        if (!(_atLogin && TrayOn))
        {
            Open(null);
        }

        if (S.Settings.WidgetEnabled && !_perf && (!_atLogin || S.Settings.WidgetAtLogin))
        {
            SyncWidget();
        }

        if (SingleInstance.Message(args) is var m && m != "show")
        {
            Message(m);   // started by a notification's button with no copy running
        }

        Timer.Request();
    }

    /// <summary>Makes the tray, the lifetime, the login entry, the widget and the notification link match the settings (at start and when they change).</summary>
    public void Apply()
    {
        var s = S.Settings;
        _life.ShutdownMode = TrayOn ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnMainWindowClose;
        if (TrayOn && _tray == null)
        {
            _tray = new TrayHost(this);
        }
        else if (!TrayOn && _tray != null)
        {
            _tray.Dispose();
            _tray = null;
        }

        var login = TrayOn && s.StartAtLogin;
        if (!_perf && login != _loginOn)
        {
            _loginOn = login;
            if (LoginItem.Apply(login) is { } problem)
            {
                Toast.Show(problem);
            }
        }

        RegisterAlerts();
        SyncWidget();
        _tray?.Update();
    }

    /// <summary>Opens the main window (made the first time), on the page it was left on or on <paramref name="view"/>.</summary>
    public void Open(string? view)
    {
        if (_main == null)
        {
            _main = new MainWindow(this) { PerfRun = _perf };
            _life.MainWindow = _main;
        }

        _main.FromTray(view);
    }

    /// <summary>The main window was hidden with ×: the first time, the notice says the app is still running.</summary>
    public TrayNotice? WentToTray()
    {
        if (S.State.TrayNoticeShown)
        {
            return null;
        }

        S.State.TrayNoticeShown = true;
        StateStore.Save(S.State);
        var n = new TrayNotice(Quit);
        n.Show();
        return n;
    }

    /// <summary>The main window really closed (the tray is off, or × quits): the app ends.</summary>
    public void MainClosed()
    {
        _main = null;
        if (!Quitting)
        {
            Quit();
        }
    }

    /// <summary>Saves state, removes the tray icon and the widget, and ends the app.</summary>
    public void Quit()
    {
        if (Quitting)
        {
            return;
        }

        Quitting = true;
        _stop.Cancel();
        _stop.Dispose();
        Timer.Stop();
        StateStore.Save(S.State);
        _widget?.Close();
        _widget = null;
        _tray?.Dispose();
        _tray = null;
        _main?.Close();
        _life.Shutdown();
    }

    public void ShowWidget(bool on)
    {
        S.Settings.WidgetEnabled = on;
        SettingsStore.Save(S.Settings);
        SyncWidget();
        _tray?.Update();
    }

    private void SyncWidget()
    {
        var on = S.Settings.WidgetEnabled && !_perf;
        if (on && _widget == null)
        {
            _widget = new UsageWidget(this);
            _widget.Closed += (_, _) => _widget = null;
            _widget.Show();
        }
        else if (!on && _widget != null)
        {
            _widget.Close();
        }
        else
        {
            _widget?.Update();
        }
    }

    /// <summary>A message from a second start or a notification's button: <c>show</c>, <c>show &lt;page&gt;</c> or <c>pause</c>.</summary>
    public void Message(string m)
    {
        if (m == "pause")
        {
            PauseAlerts(true);
        }
        else if (m.StartsWith("show", StringComparison.Ordinal) && !Quitting)
        {
            Open(m.Length > 5 ? m[5..] : null);
        }
    }

    private void Changed(string appId)
    {
        _tray?.Update();
        _widget?.Update();
        CheckAlerts(appId);
    }

    /// <summary>The widget's blocks in the order of <see cref="Settings.LimitsEnabled"/>. For the tray ring with no window shown, every window counts.</summary>
    public List<WidgetApp> WidgetApps(bool forTray = false)
    {
        var now = DateTime.Now;
        List<WidgetApp> Build(bool all) => [.. S.Settings.LimitsEnabled.Select(S.Def).OfType<AppDefinition>().Where(LimitsView.Has)
            .Select(d => WidgetRows.Build(d.Id, d.Name, Limits.Status(d.Id), Limits.History.Readings(d.Id), w => all || WidgetRows.Shows(S.Settings, d.Id, w), now))
            .OfType<WidgetApp>()];
        var shown = Build(false);
        return forTray && shown.Count == 0 ? Build(true) : shown;
    }
}
