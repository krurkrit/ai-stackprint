// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;

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
            case "Apps":
                _apps.Leave();
                break;
            case "Plugin":
                _plugins.Leave();
                break;
            case "Skill":
                _skills.Leave();
                break;
            case "Part":
                _parts.Leave();
                break;
            case "Mcp":
                _mcp.Leave();
                break;
            case "Hooks":
                _hooks.Leave();
                break;
            case "Usage":
                _usage.Leave();
                break;
            case var v when v.StartsWith("Models"):
                _models.Leave();
                break;
            case var v when v.StartsWith("App:"):
                _detail.Leave();
                break;
            case var v when v.StartsWith("Project:"):
                _project.Leave();
                break;
        }
    }

    private void AddApp() => Fire.Forget(AddAppAsync());

    private async Task AddAppAsync()
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
    private void LoadProjectHealth(List<AppDefinition> defs, List<string> projects) => Fire.Forget(LoadProjectHealthAsync(defs, projects));

    private async Task LoadProjectHealthAsync(List<AppDefinition> defs, List<string> projects)
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
}
