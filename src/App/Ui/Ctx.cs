// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;

namespace AiStackprint.Ui;

/// <summary>What every view needs: state, navigation, rescan and the owner window for dialogs.</summary>
internal class Ctx(AppState s, Window owner, Action<string> go, Action rescan)
{
    public AppState S { get; } = s;
    public Window Owner { get; } = owner;
    public void Go(string view) => go(view);
    public void Rescan() => rescan();
    public bool SettingsChanged { get; set; }
    public Action AddApp { get; init; } = () => { };
    public Action AddProject { get; init; } = () => { };
    /// <summary>Re-applies Appearance settings and rebuilds the UI.</summary>
    public Action Restyle { get; init; } = () => { };
    /// <summary>Reads settings, projects and apps from their files again and rescans (after an import).</summary>
    public Action Reload { get; init; } = () => { };
    /// <summary>Rebuilds the current page from the last scan (no rescan).</summary>
    public Action Refresh { get; init; } = () => { };
    /// <summary>Opens a skill or plugin on its page with the detail panel showing it.</summary>
    public Action<Item> Open { get; init; } = _ => { };
    /// <summary>Opens the Skills or Plugins page filtered to an app and/or a search text.</summary>
    public Action<string, string?, string?> Browse { get; init; } = (_, _, _) => { };
    /// <summary>Esc on the current page (closes an open panel); returns true when handled. Reset on every page change.</summary>
    public Func<bool>? Escape { get; set; }
    /// <summary>Live usage limits, shared by the dashboard and app pages.</summary>
    public LimitsService Limits { get; init; } = null!;

    public AppInfo? Info(string appId) => S.Apps.FirstOrDefault(a => a.Id == appId);
    private List<Item>? _countsFor;
    private Dictionary<(string, string), int> _counts = [];
    /// <summary>Items per app and kind; counted once per scan, not once per call.</summary>
    public int Count(string appId, string kind)
    {
        if (!ReferenceEquals(_countsFor, S.Items))
        {
            _countsFor = S.Items;
            _counts = S.Items.GroupBy(i => (i.App, i.Kind)).ToDictionary(g => g.Key, g => g.Count());
        }
        return _counts.GetValueOrDefault((appId, kind));
    }

    /// <summary>Apps to show: scanned apps, minus not-installed ones when the setting is on.</summary>
    public IEnumerable<AppInfo> ShownApps => S.Apps.Where(a => a.Installed || !S.Settings.HideNotInstalled);

    /// <summary>An app's name from its id (the id for an app no longer defined).</summary>
    public string AppName(string id) => S.Def(id)?.Name ?? id;

    public static string ProjectName(string path) => Path.GetFileName(path.TrimEnd('/', '\\'));
}
