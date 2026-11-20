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

    /// <summary>After an import: settings, projects and apps are read from their files again, then a rescan.</summary>
    private void Reload()
    {
        _s.Settings = SettingsStore.Load();
        Table.VirtualFrom = _s.Settings.VirtualListRows;
        _s.Projects.Clear();
        _s.ProjectAdded.Clear();
        LoadProjects();
        _s.Use(AppCatalog.Read());
        Ui.Theme.Appearance(Application.Current!, _s.Settings);
        Scan();
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
            "Dashboard" => DashboardView.Build(_x),
            "Apps" => _apps.Build(),
            "Settings" => _settings.Build(),
            "Plugin" => _plugins.Build(),
            "Skill" => _skills.Build(),
            "Part" => _parts.Build(),
            "Health" => _health.Build(),
            "Mcp" => _mcp.Build(),
            "Hooks" => _hooks.Build(),
            "Usage" => _usage.Build(),
            "Models" => _models.Build(),
            _ when _view.StartsWith("Models:") => _models.Build(_view[7..]),
            "Logs" => _logs.Build(null),
            _ when _view.StartsWith("Logs:") => _logs.Build(_view[5..]),
            _ when _view.StartsWith("App:") => _detail.Build(_view[4..]),
            _ when _view.StartsWith("Project:") => _project.Build(_view[8..]),
            _ => DashboardView.Build(_x)
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
            case "Part": _parts.Leave(); break;
            case "Mcp": _mcp.Leave(); break;
            case "Hooks": _hooks.Leave(); break;
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

    private int _healthRun;

    /// <summary>
    /// The projects' checks, after the dashboard shows (they walk every project folder, and only the Health tile, the badge and the Health page need them).
    /// A newer scan makes an older run's result stale: it is dropped.
    /// </summary>
    private async void LoadProjectHealth(List<AppDefinition> defs, List<string> projects)
    {
        var run = ++_healthRun;
        var found = await Task.Run(() => ProjectChecks.ForHealth(defs, projects, PathUtil.Home));
        if (run != _healthRun)
        {
            return;
        }

        _s.ProjectHealth = found;
        RefreshNav();
        if (_view is "Dashboard" or "Health")
        {
            Refresh();
        }
    }

    private async void Scan()
    {
        if (_scanning)
        {
            return;
        }

        _scanning = true; _s.ProjectHealth = null; RefreshNav();   // the projects' checks run after the dashboard shows
        ProjectFiles.Forget();   // a scan lists each project folder anew
        var defs = SettingsStore.Visible(_s.Defs, _s.Settings); var projects = _s.Projects.Where(Directory.Exists).ToList();   // a missing folder is greyed in the menu until relocated or removed
        var status = _status;   // set while the first scan's loading dashboard is shown
        void Say(string text) { if (status != null) { Dispatcher.UIThread.Post(() => status(text)); } }
        var steps = new List<(string, long)>(); var watch = System.Diagnostics.Stopwatch.StartNew();
        void Step(string name) { steps.Add((name, watch.ElapsedMilliseconds)); watch.Restart(); }   // each step's time, for the --perf run
        var claudeModel = defs.FirstOrDefault(d => d.Id == "claude-code") is { } cd ? Task.Run(() => UsageStats.LastModel(LogReader.Find(cd, projects, usage: true))) : Task.FromResult<string?>(null);   // its window sets the skill-list budget; finding the logs runs beside the scan
        var r = await Task.Run(() => { ScanCache.Load(); Step("cache load"); var c = Scanner.Collect(defs, projects); Step("collect"); return c; });   // the safety and change results of the last run: only changed files are read again
        _s.Items = r.Items; _s.Apps = r.Apps;
        var items = _s.Items;
        (_s.Changes, _s.Health, _s.Mcp, _s.Hooks) = await Task.Run(async () =>
        {
            Say(DashboardView.Comparing);
            watch.Restart();   // not the wait for the UI thread between the two
            var mcp = McpServers.Read(items).Concat(McpServers.FromPlugins(items)).ToList(); Step("mcp");
            var hooks = Hooks.Read(items, PathUtil.Home).Concat(Hooks.FromPlugins(items, PathUtil.Home)).ToList(); Step("hooks");
            // The four checks read the scan's items and do not depend on each other, so they run side by side (the scan cache is thread-safe);
            // each step's time is its own, so together they add up to more than the wall time
            var timed = new List<(string, long)>();
            Task<T> Run<T>(string name, Func<T> check) => Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var v = check();
                lock (timed) { timed.Add((name, sw.ElapsedMilliseconds)); }
                return v;
            });
            var changes = Run("changes", () => ScanDiff.Update(items, ScanDiff.File, mcp, hooks));
            var health = Run("health", () => Health.Compute(items, claudeModel.GetAwaiter().GetResult()));
            var safety = Run("safety", () => SafetyScan.Check(items, PathUtil.Home, (done, total) => { if (done == 1 || done == total || done % 10 == 0) { Say(DashboardView.Safety(done, total)); } }));
            var checks = Run("configs", () => ConfigChecks.ForHealth(items, PathUtil.Home).Concat(ModelRetirement.ForHealth(items, DateOnly.FromDateTime(DateTime.Today))).ToList());
            await Task.WhenAll(changes, health, safety, checks).ConfigureAwait(false);
            steps.AddRange(timed); watch.Restart();
            var all = health.Result.Concat(safety.Result).Concat(checks.Result).ToList();
            ScanCache.Save(); Step("cache save");
            return (changes.Result, all, mcp, hooks);
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
        if (!first)
        {
            Toast.Show($"Scanned: {ReadableLog.Plural(_s.Apps.Count(a => a.Installed), "app")}, {ReadableLog.Plural(_s.Items.Count(i => i.Kind == "Skill"), "skill")}, {ReadableLog.Plural(_s.Items.Count(i => i.Kind == "Plugin"), "plugin")}", "scan_text");
        }

        RequestLimits();
        LoadProjectHealth(defs, projects);
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
