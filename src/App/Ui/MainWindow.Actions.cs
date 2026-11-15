// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Threading;

namespace AiStackprint.Ui;

internal partial class MainWindow
{
    /// <summary>Re-applies Appearance settings and rebuilds the UI so sizes and weights take effect.</summary>
    private void Restyle()
    {
        Ui.Theme.Appearance(Application.Current!, _s.Settings);
        Refresh();
    }

    private bool _firstShow = true;
    private Action<string>? _status;   // the loading dashboard's "what the scan is doing" text, during the first scan

    private void Refresh()
    {
        if (!_scanned)
        {
            return;   // the first scan shows its own loading line
        }

        RefreshNav();
        UpdateHistoryButtons();
        _x.Escape = null;
        var inLogs = _view == "Logs" || _view.StartsWith("Logs:");
        var leftLogs = _wasLogs && !inLogs;
        _wasLogs = inLogs;
        if (leftLogs)
        {
            _logs.Release();   // the Logs page keeps no file text, controls or watcher once it is closed
        }

        if (_shownView != _view)
        {
            Leave(_shownView);
            _shownView = _view;
        }

        _body.Content = _view switch
        {
            "Dashboard" => DashboardView.Build(_x, AddProject),
            "Apps" => _apps.Build(),
            "Settings" => _settings.Build(),
            "Plugin" => _plugins.Build(),
            "Skill" => _skills.Build(),
            "Health" => _health.Build(),
            "Mcp" => _mcp.Build(),
            "Usage" => _usage.Build(),
            "Models" => _models.Build(),
            _ when _view.StartsWith("Models:") => _models.Build(_view[7..]),
            "Logs" => _logs.Build(null),
            _ when _view.StartsWith("Logs:") => _logs.Build(_view[5..]),
            _ when _view.StartsWith("App:") => _detail.Build(_view[4..]),
            _ when _view.StartsWith("Project:") => _project.Build(_view[8..]),
            _ => DashboardView.Build(_x, AddProject)
        };
        if (_firstShow)
        {
            _firstShow = false;
            if (_body.Content is Avalonia.Controls.Control first)
            {
                Reveal.Fade(first);   // the first scan is done: the data fades in over the loading cards
            }
        }

        MemoryTrim.Request();   // the page that was just replaced
    }

    private string _shownView = "";

    /// <summary>
    /// Another page opens: the view that was shown lets go of the controls it keeps, which would otherwise keep its whole page in memory.
    /// A refresh of the same page (a rescan) keeps them, so the search box keeps its focus and text.
    /// </summary>
    private void Leave(string view)
    {
        switch (view)
        {
            case "Apps": _apps.Leave(); break;
            case "Plugin": _plugins.Leave(); break;
            case "Skill": _skills.Leave(); break;
            case "Mcp": _mcp.Leave(); break;
            case "Usage": _usage.Leave(); break;
            case var v when v.StartsWith("Models"): _models.Leave(); break;
            case var v when v.StartsWith("App:"): _detail.Leave(); break;
            case var v when v.StartsWith("Project:"): _project.Leave(); break;
        }
    }

    private async void AddApp()
    {
        var def = await new AddAppDialog().ShowDialog<AppDefinition?>(this);
        if (def == null)
        {
            return;
        }

        if (AppCatalog.SaveUser(def) is { } refused)
        {
            Toast.Show(refused, "warning");   // the dialog checks first; this is the same check at save time
            return;
        }

        _s.Use(AppCatalog.Read());
        _view = "App:" + def.Id;
        Scan();
    }

    private async void Scan()
    {
        if (_scanning)
        {
            return;
        }

        _scanning = true; RefreshNav();
        var defs = SettingsStore.Visible(_s.Defs, _s.Settings); var projects = _s.Projects.Where(Directory.Exists).ToList();   // a missing folder is greyed in the menu until relocated or removed
        var status = _status;   // set while the first scan's loading dashboard is shown
        void Say(string text) { if (status != null) { Dispatcher.UIThread.Post(() => status(text)); } }
        var steps = new List<(string, long)>(); var watch = System.Diagnostics.Stopwatch.StartNew();
        void Step(string name) { steps.Add((name, watch.ElapsedMilliseconds)); watch.Restart(); }   // each step's time, for the --perf run
        var r = await Task.Run(() => { ScanCache.Load(); Step("cache load"); var c = Scanner.Collect(defs, projects); Step("collect"); return c; });   // the safety and change results of the last run: only changed files are read again
        _s.Items = r.Items; _s.Apps = r.Apps;
        var items = _s.Items;
        (_s.Changes, _s.Health, _s.Mcp) = await Task.Run(() =>
        {
            Say(DashboardView.Comparing);
            watch.Restart();   // not the wait for the UI thread between the two
            var mcp = McpServers.Read(items); Step("mcp");
            var changes = ScanDiff.Update(items, ScanDiff.File, mcp); Step("changes");
            var health = Health.Compute(items); Step("health");
            var safety = SafetyScan.Check(items, PathUtil.Home, (done, total) => { if (done == 1 || done == total || done % 10 == 0) { Say(DashboardView.Safety(done, total)); } }); Step("safety");
            Say(DashboardView.Configs);
            var checks = ConfigChecks.ForHealth(items, PathUtil.Home).Concat(ProjectChecks.ForHealth(defs, projects, PathUtil.Home)).ToList(); Step("configs");
            var all = health.Concat(safety).Concat(checks).ToList();
            ScanCache.Save(); Step("cache save");
            return (changes, all, mcp);
        });
        _status = null;
        var first = !_scanned;
        if (first)
        {
            _perfScanMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(_perfOpened).TotalMilliseconds;
            _perfSteps = steps;
        }
        _scanning = false; _scanned = true;
        Refresh();
        RequestLimits();
        if (first && PerfRun)
        {
            RunPerf();   // no update check or crash notice in a measuring run
            return;
        }

        if (first)
        {
            UpdateView.Startup(_x);
            foreach (var problem in new[] { DataFolder.MoveProblem, SettingsStore.Problem, ProjectStore.Problem, AppCatalog.Problem, ModelCatalog.Problem })
            {
                if (problem != null)
                {
                    Toast.Show(problem, "warning", 10);
                }
            }
        }

        if (first && CrashLog.TakeNew())
        {
            CrashNotice.Show(_x);
        }
    }
}
