// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Platform.Storage;

namespace AiStackprint.Ui;

internal partial class MainWindow
{
    /// <summary>Re-applies Appearance settings and rebuilds the UI so sizes and weights take effect.</summary>
    private void Restyle()
    {
        Ui.Theme.Appearance(Application.Current!, _s.Settings);
        Refresh();
    }

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
            "Logs" => _logs.Build(null),
            _ when _view.StartsWith("Logs:") => _logs.Build(_view[5..]),
            _ when _view.StartsWith("App:") => _detail.Build(_view[4..]),
            _ when _view.StartsWith("Project:") => _project.Build(_view[8..]),
            _ => DashboardView.Build(_x, AddProject)
        };
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
        var r = await Task.Run(() => Scanner.Collect(defs, projects));
        _s.Items = r.Items; _s.Apps = r.Apps;
        var items = _s.Items;
        (_s.Changes, _s.Health, _s.Mcp) = await Task.Run(() => (ScanDiff.Update(items, ScanDiff.File),
            Health.Compute(items).Concat(ProjectChecks.ForHealth(defs, projects, PathUtil.Home)).ToList(), McpServers.Read(items)));
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
