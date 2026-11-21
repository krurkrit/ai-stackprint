// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia.Threading;

namespace AiStackprint.Ui;

internal partial class MainWindow
{
    private static readonly TimeSpan _settle = TimeSpan.FromSeconds(1);

    private void Scan() => Fire.Forget(ScanAsync());

    private async Task ScanAsync()
    {
        if (_scanning)
        {
            return;
        }

        _scanning = true;
        _s.ProjectHealth = null;
        RefreshNav();   // the projects' checks run after the dashboard shows
        ProjectFiles.Forget();   // a scan lists each project folder anew
        var defs = SettingsStore.Visible(_s.Defs, _s.Settings);
        var projects = _s.Projects.Where(Directory.Exists).ToList();   // a missing folder is greyed in the menu until relocated or removed
        var status = _status;   // set while the first scan's loading dashboard is shown
        void Say(string text) { if (status != null) { Dispatcher.UIThread.Post(() => status(text)); } }
        var steps = new List<(string, long)>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        void Step(string name)
        {
            steps.Add((name, watch.ElapsedMilliseconds));
            watch.Restart();
        }   // each step's time, for the --perf run
        var claudeModel = defs.FirstOrDefault(d => d.Id == "claude-code") is { } cd ? Task.Run(() => UsageStats.LastModel(LogReader.Find(cd, projects, usage: true))) : Task.FromResult<string?>(null);   // its window sets the skill-list budget; finding the logs runs beside the scan
        var r = await Task.Run(() =>
        {
            ScanCache.Load();
            Step("cache load");
            var c = Scanner.Collect(defs, projects);
            Step("collect");
            return c;
        });   // the safety and change results of the last run: only changed files are read again
        _s.Items = r.Items;
        _s.Apps = r.Apps;
        var items = _s.Items;
        (_s.Changes, _s.Health, _s.Mcp, _s.Hooks) = await Task.Run(async () =>
        {
            Say(DashboardView.Comparing);
            watch.Restart();   // not the wait for the UI thread between the two
            var mcp = McpServers.Read(items).Concat(McpServers.FromPlugins(items)).ToList();
            Step("mcp");
            var hooks = Hooks.Read(items, PathUtil.Home).Concat(Hooks.FromPlugins(items, PathUtil.Home)).ToList();
            Step("hooks");
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
            steps.AddRange(timed);
            watch.Restart();
            var all = health.Result.Concat(safety.Result).Concat(checks.Result).ToList();
            ScanCache.Save();
            Step("cache save");
            return (changes.Result, all, mcp, hooks);
        });
        _status = null;
        var first = !_scanned;
        if (first)
        {
            _perfScanMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(_perfOpened).TotalMilliseconds;
            _perfSteps = steps;
        }
        _scanning = false;
        _scanned = true;
        Refresh();
        if (!first)
        {
            Toast.Show($"Scanned: {ReadableLog.Plural(_s.Apps.Count(a => a.Installed), "app")}, {ReadableLog.Plural(_s.Items.Count(i => i.Kind == "Skill"), "skill")}, {ReadableLog.Plural(_s.Items.Count(i => i.Kind == "Plugin"), "plugin")}", "scan_text");
        }

        if (first && !PerfRun)
        {
            await Task.Delay(_settle);   // the dashboard's first animation (the donut unfolds) runs on a quiet CPU: the project checks start after it
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
