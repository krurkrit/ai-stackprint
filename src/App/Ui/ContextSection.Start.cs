// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AiStackprint.Ui;

/// <summary>Where the session starts: the project folder, a folder below it with its own instruction files, or any folder picked inside it.</summary>
internal partial class ContextSection
{
    /// <summary>The folders below the project that hold instruction files (<see cref="ProjectChecks.StartFolders"/>), then "Other folder…".</summary>
    private void StartMenu(string project, Control at)
    {
        var menu = new ContextMenu();
        void Add(string header, Action pick)
        {
            var m = new MenuItem { Header = header };
            m.Click += (_, _) => pick();
            menu.Items.Add(m);
        }
        if (!ContextBudget.Same(_start!, project))
        {
            Add("Project folder", () => Use(project));
        }

        foreach (var f in ProjectChecks.StartFolders(project, x.S.Defs))
        {
            Add(Path.GetRelativePath(project, f).Replace('\\', '/'), () => Use(f));
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        Add("Other folder…", () => PickStart(project));
        menu.Open(at);
    }

    private void Use(string start)
    {
        _start = start;
        _last = null;
        _preview = null;
        x.Refresh();
    }

    private void PickStart(string project) => Fire.Forget(PickStartAsync(project));

    private async Task PickStartAsync(string project)
    {
        var start = await x.Owner.StorageProvider.TryGetFolderFromPathAsync(project);
        var r = await x.Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Session starts in",
            SuggestedStartLocation = start
        });
        if (r.Count == 0 || r[0].TryGetLocalPath() is not { } p)
        {
            return;
        }

        if (!ContextBudget.Same(p, project) && !ContextBudget.Inside(p, project))
        {
            Toast.Show("Pick a folder inside the project", "warning");
            return;
        }
        Use(p);
    }
}
