// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Project folders: kept in projects.json; a folder that no longer exists is greyed in the menu, to relocate (click) or remove (bin).</summary>
internal partial class MainWindow
{
    private void LoadProjects()
    {
        foreach (var p in ProjectStore.Load())
        {
            _s.Projects.Add(p.Path);
            _s.ProjectAdded[p.Path] = p.Added;
        }
    }

    private void SaveProjects() =>
        ProjectStore.Save(_s.Projects.Select(p => new SavedProject(p, _s.ProjectAdded.GetValueOrDefault(p, DateTime.UtcNow))));

    private async Task<string?> PickFolder(string title)
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return r.Count == 0 ? null : r[0].TryGetLocalPath();
    }

    private void AddProject() => Fire.Forget(AddProjectAsync());

    private async Task AddProjectAsync()
    {
        if (await PickFolder("Project folder") is not { } p)
        {
            return;
        }

        if (_s.Projects.Find(o => ProjectStore.Same(o, p)) is { } known)
        {
            await Dialogs.Message(this, "Project already added", $"{Ctx.ProjectName(known)} is already in the list ({known}). It opens instead; a folder is listed once.", warn: true);
            Go("Project:" + known);
            return;
        }

        _s.Projects.Add(p);
        _s.ProjectAdded[p] = DateTime.UtcNow;
        SaveProjects();
        _view = "Project:" + p;
        Scan();
    }

    /// <summary>Asks first; only the entry in the list goes, never the folder.</summary>
    private void RemoveProject(string p) => Fire.Forget(RemoveProjectAsync(p));

    private async Task RemoveProjectAsync(string p)
    {
        if (!await Dialogs.Confirm(this, "Remove project", $"Remove {Ctx.ProjectName(p)} from the list?\n{p}\n\nThe folder and its files are not touched; add it again at any time.", "Remove", danger: true))
        {
            return;
        }

        _s.Projects.Remove(p);
        _s.ProjectAdded.Remove(p);
        SaveProjects();
        _view = "Dashboard";
        Scan();
    }

    /// <summary>The folder was moved: the chosen folder takes the old one's place in the list and keeps its added date.</summary>
    private void RelocateProject(string old) => Fire.Forget(RelocateProjectAsync(old));

    private async Task RelocateProjectAsync(string old)
    {
        if (await PickFolder($"New location of {Ctx.ProjectName(old)}") is not { } p)
        {
            return;
        }

        var i = _s.Projects.IndexOf(old);
        if (i < 0)
        {
            return;
        }

        if (_s.Projects.Find(o => o != old && ProjectStore.Same(o, p)) is { } known)
        {
            await Dialogs.Message(this, "Project already added", $"{Ctx.ProjectName(known)} is already in the list ({known}). Choose another folder, or remove {Ctx.ProjectName(old)} from the list.", warn: true);
            return;
        }

        _s.Projects[i] = p;
        _s.ProjectAdded[p] = _s.ProjectAdded.GetValueOrDefault(old, DateTime.UtcNow);
        if (p != old)
        {
            _s.ProjectAdded.Remove(old);
        }

        SaveProjects();
        _view = "Project:" + p;
        Scan();
    }

    /// <summary>A project in the menu; a missing folder is greyed, a click picks its new location and a bin removes it from the list.</summary>
    private Control ProjectRow(string p)
    {
        if (Directory.Exists(p))
        {
            return Go("folder", Ctx.ProjectName(p), "Project:" + p).With(b => ToolTip.SetTip(b, p));
        }

        var row = NavBtn("folder", Ctx.ProjectName(p), () => RelocateProject(p)).Themed(Button.ForegroundProperty, Muted);
        ToolTip.SetTip(row, $"Folder not found: {p}\nClick to choose where it moved.");
        if (!_navOpen)
        {
            return row;
        }

        var bin = IconBtn("delete", "Remove from the list", () => RemoveProject(p));
        var dock = new DockPanel();
        DockPanel.SetDock(bin, Dock.Right);
        dock.Children.Add(bin);
        dock.Children.Add(row);
        return dock;
    }
}
