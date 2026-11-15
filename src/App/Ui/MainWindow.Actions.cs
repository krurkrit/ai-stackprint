// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Platform.Storage;
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

    private async void AddProject()
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Project folder" });
        if (r.Count == 0 || r[0].TryGetLocalPath() is not { } p || _s.Projects.Contains(p))
        {
            return;
        }

        _s.Projects.Add(p);
        _view = "Project:" + p;
        Scan();
    }

    private void RemoveProject(string p)
    {
        _s.Projects.Remove(p);
        _view = "Dashboard";
        Scan();
    }

    private async void AddApp()
    {
        var def = await new AddAppDialog().ShowDialog<AppDefinition?>(this);
        if (def == null)
        {
            return;
        }

        AppCatalog.SaveUser(def);
        _s.Defs = AppCatalog.Load();
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
        var defs = SettingsStore.Visible(_s.Defs, _s.Settings); var projects = _s.Projects.ToList();
        var status = _status;   // set while the first scan's loading dashboard is shown
        void Say(string text) { if (status != null) { Dispatcher.UIThread.Post(() => status(text)); } }
        var r = await Task.Run(() => { ScanCache.Load(); return Scanner.Collect(defs, projects); });   // the safety and change results of the last run: only changed files are read again
        _s.Items = r.Items; _s.Apps = r.Apps;
        var items = _s.Items;
        (_s.Changes, _s.Health, _s.Mcp) = await Task.Run(() =>
        {
            Say(DashboardView.Comparing);
            var mcp = McpServers.Read(items);
            var changes = ScanDiff.Update(items, ScanDiff.File, mcp);
            var health = Health.Compute(items);
            var safety = SafetyScan.Check(items, PathUtil.Home, (done, total) => { if (done == 1 || done == total || done % 10 == 0) { Say(DashboardView.Safety(done, total)); } });
            Say(DashboardView.Configs);
            var checks = ConfigChecks.ForHealth(items, PathUtil.Home).Concat(ProjectChecks.ForHealth(defs, projects, PathUtil.Home));
            var all = health.Concat(safety).Concat(checks).ToList();
            ScanCache.Save();
            return (changes, all, mcp);
        });
        _status = null;
        var first = !_scanned;
        _scanning = false; _scanned = true;
        Refresh();
        RequestLimits();
        if (first)
        {
            UpdateView.Startup(_x);
        }

        if (first && CrashLog.TakeNew())
        {
            CrashNotice.Show(_x);
        }
    }
}
