// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Lets the user register any app (e.g. a new orchestrator) by pointing at its config folder.</summary>
internal class AddAppDialog : Window
{
    private readonly TextBox _name = new() { PlaceholderText = "App name, e.g. My Orchestrator" };
    private readonly TextBox _folder = new() { PlaceholderText = "Config folder, e.g. ~/.myapp (used to detect and scan)" };
    private readonly TextBox _proj = new() { PlaceholderText = "Optional project-relative skills folder, e.g. .myapp/skills" };
    private readonly TextBlock _refused = Txt("", TextSize.Small, false, ErrorKey).With(t => t.IsVisible = false);
    private readonly ComboBox _cat = new() { ItemsSource = new[] { "orchestrator", "agent", "editor", "chat", "runtime", "custom" }, SelectedIndex = 0 };

    public AddAppDialog()
    {
        var browse = Btn("Browse...", async () =>
        {
            var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "App config folder" });
            if (r.Count > 0 && r[0].TryGetLocalPath() is { } p)
            {
                _folder.Text = p;
            }
        });
        var ok = Btn("Save", Save, true);
        ok.IsDefault = true;
        var body = new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                _name, _cat,
                new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Space.M, Children = { _folder, browse.With(b => Grid.SetColumn(b, 1)) } },
                _proj,
                _refused,
                Txt("The config folder is scanned (depth 4) for SKILL.md files at User level. Saved to apps.json; edit it for finer control.", TextSize.Caption, false, Muted)
            }
        };
        Dialogs.Chrome(this, "Add an app to scan", body, Widths.DialogM, Btn("Cancel", () => Close(null)).With(b => b.IsCancel = true), ok);
    }

    private void Save()
    {
        var name = (_name.Text ?? "").Trim();
        var folder = (_folder.Text ?? "").Trim();
        if (name == "" || folder == "")
        {
            return;
        }

        var def = new AppDefinition
        {
            Id = AppCatalog.CustomPrefix + new string([.. name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]).Trim('-'),
            Name = name,
            Category = _cat.SelectedItem as string ?? "custom",
            Detect = { folder },
            Roots = { new ScanRoot { Path = folder, Level = "User", Depth = 4 } }
        };
        if (!string.IsNullOrWhiteSpace(_proj.Text))
        {
            def.ProjectRoots.Add(new ScanRoot { Path = _proj.Text.Trim(), Level = "Project" });
        }

        if (AppCatalog.Refuse(def) is { } why)
        {
            _refused.Text = why;   // a tool AI Stackprint already supports: no second copy of it
            _refused.IsVisible = true;
            return;
        }

        Close(def);
    }
}
