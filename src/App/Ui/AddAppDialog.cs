using AiStackprint.Models;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Lets the user register any app (e.g. a new orchestrator) by pointing at its config folder.</summary>
class AddAppDialog : Window
{
    readonly TextBox _name = new() { PlaceholderText = "App name, e.g. My Orchestrator" };
    readonly TextBox _folder = new() { PlaceholderText = "Config folder, e.g. ~/.myapp (used to detect and scan)" };
    readonly TextBox _proj = new() { PlaceholderText = "Optional project-relative skills folder, e.g. .myapp/skills" };
    readonly ComboBox _cat = new() { ItemsSource = new[] { "orchestrator", "agent", "editor", "chat", "runtime", "custom" }, SelectedIndex = 0 };

    public AddAppDialog()
    {
        Title = "Add app"; Width = 520; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var browse = Btn("Browse...", async () =>
        {
            var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "App config folder" });
            if (r.Count > 0 && r[0].TryGetLocalPath() is { } p) _folder.Text = p;
        });
        var ok = Btn("Save", Save, true);
        ok.IsDefault = true;
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                Txt("Add an app to scan", 18, true), _name, _cat,
                new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { _folder, browse.With(b => Grid.SetColumn(b, 1)) } },
                _proj,
                Txt("The config folder is scanned (depth 4) for SKILL.md files at User level. Saved to apps.json; edit it for finer control.", 12, false, Muted),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { Btn("Cancel", () => Close(null)), ok } }
            }
        };
    }

    void Save()
    {
        var name = (_name.Text ?? "").Trim();
        var folder = (_folder.Text ?? "").Trim();
        if (name == "" || folder == "") return;
        var def = new AppDefinition
        {
            Id = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-'),
            Name = name,
            Category = _cat.SelectedItem as string ?? "custom",
            Detect = { folder },
            Roots = { new ScanRoot { Path = folder, Level = "User", Depth = 4 } }
        };
        if (!string.IsNullOrWhiteSpace(_proj.Text)) def.ProjectRoots.Add(new ScanRoot { Path = _proj.Text.Trim(), Level = "Project" });
        Close(def);
    }
}
