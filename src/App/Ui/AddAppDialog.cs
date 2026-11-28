// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Lets the user register an app AI Stackprint does not know, laid out like its entry in apps.json: name, category, the config folder that
/// detects it, then one section per list (skills, configs, commands, agents, logs) with a row per path. The config folder is looked into
/// (<see cref="AppFolderProbe"/>) and what it holds is filled in as rows the user can change, remove or add to.
/// </summary>
internal class AddAppDialog : Window
{
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(250);

    private static readonly Dictionary<string, (string Title, string Hint, string Placeholder)> _sections = new()
    {
        ["skills"] = ("Skills", "Folders of skills (a SKILL.md in each).", "e.g. ~/.myapp/skills"),
        ["configs"] = ("Config files", "Instruction, settings, MCP, ignore and hook files.", "e.g. ~/.myapp/AGENTS.md"),
        ["commands"] = ("Commands", "Folders of command files (saved prompts).", "e.g. .myapp/commands"),
        ["agents"] = ("Agents", "Folders of agent files (sub-agents).", "e.g. ~/.myapp/agents"),
        ["logs"] = ("Logs", "Log files, or folders of them.", "e.g. ~/.myapp/logs")
    };

    private readonly TextBox _name = new() { PlaceholderText = "e.g. My Agent" };
    private readonly TextBox _folder = new() { PlaceholderText = "e.g. ~/.myapp" };
    private readonly TextBlock _status = Txt("", TextSize.Caption, false, Muted);
    private readonly TextBlock _refused = Txt("", TextSize.Small, false, ErrorKey).With(t => t.IsVisible = false);
    private readonly ComboBox _cat = new()
    {
        ItemsSource = AppCategories.Order,   // the same kinds, A to Z, as the Category menu and help on the Apps page
        SelectedItem = "agent",              // most new tools are agents
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private readonly Dictionary<string, StackPanel> _lists = [];
    private readonly List<EntryRow> _rows = [];
    private int _look;

    /// <summary>One row: the entry's controls, and whether it was filled in from the folder (replaced when the folder changes) or by the user.</summary>
    private sealed class EntryRow(string list, TextBox path, ComboBox level, ComboBox? kind, Control view)
    {
        public string List { get; } = list;
        public TextBox Path { get; } = path;
        public ComboBox Level { get; } = level;
        public ComboBox? Kind { get; } = kind;
        public Control View { get; } = view;
        public bool Found { get; set; }

        public AppEntry Entry => new(List, Path.Text ?? "", Level.SelectedItem as string ?? "User", Kind?.SelectedItem as string ?? "");
    }

    public AddAppDialog()
    {
        var browse = Btn("Browse...", () => Fire.Forget(BrowseAsync()));
        var ok = Btn("Save", Save, true);
        ok.IsDefault = true;
        _folder.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Look(_folder.Text ?? "");   // typed, pasted, or picked with Browse
            }
        };

        var form = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Widths.LabelS},*"),
            ColumnSpacing = Space.M,
            RowSpacing = Space.M
        };
        AddRow(form, "Name", _name);
        AddRow(form, "Category", _cat);
        AddRow(form, "Config folder", new StackPanel
        {
            Spacing = Space.XS,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = Space.M,
                    Children = { _folder, browse.With(b => Grid.SetColumn(b, 1)) }
                },
                _status
            }
        });

        var sections = new StackPanel { Spacing = Space.L };
        foreach (var list in AppFolderProbe.Lists)
        {
            sections.Children.Add(Section(list));
        }

        ShowStatus();
        var body = new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                form,
                new ScrollViewer
                {
                    MaxHeight = Heights.Viewer,
                    Content = sections
                },
                _refused,
                Txt("Saved to apps.json in the same lists. Project and Local paths are relative to each project folder; ~ is your home folder.", TextSize.Caption, false, Muted)
                    .With(t => t.TextWrapping = TextWrapping.Wrap)
            }
        };
        Dialogs.Chrome(this, null, "Add an app to scan", body, Widths.DialogL, Btn("Cancel", () => Close(null)).With(b => b.IsCancel = true), ok);
    }

    /// <summary>Every row with a path, in apps.json order.</summary>
    internal IReadOnlyList<AppEntry> Entries => [.. AppFolderProbe.Lists.SelectMany(l => _rows.Where(r => r.List == l)).Select(r => r.Entry).Where(e => e.Path.Trim() != "")];

    private static void AddRow(Grid form, string label, Control input)
    {
        var row = form.RowDefinitions.Count;
        form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var text = Txt(label, TextSize.Small).With(t => t.VerticalAlignment = VerticalAlignment.Top);
        text.Margin = new Avalonia.Thickness(0, Space.S, 0, 0);   // level with the text in the box beside it
        Grid.SetRow(text, row);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        form.Children.Add(text);
        form.Children.Add(input);
    }

    /// <summary>A list's heading (its name and its apps.json key), Add, its rows and what goes in it.</summary>
    private Control Section(string list)
    {
        var (title, hint, _) = _sections[list];
        var rows = new StackPanel { Spacing = Space.S };
        _lists[list] = rows;
        var add = Btn("Add", () => AddEntry(new AppEntry(list, "", "User", list == "configs" ? "instructions" : ""), false).Path.Focus(), icon: "add");
        var head = new DockPanel();
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        head.Children.Add(Row(Txt(title, TextSize.Small, true), Txt($"\"{list}\"", TextSize.Caption, false, Muted)).With(r => r.VerticalAlignment = VerticalAlignment.Center));
        return new StackPanel
        {
            Spacing = Space.XS,
            Children = { head, Txt(hint, TextSize.Caption, false, Muted), rows }
        };
    }

    private EntryRow AddEntry(AppEntry e, bool found)
    {
        var path = new TextBox
        {
            Text = e.Path,
            PlaceholderText = _sections[e.List].Placeholder
        };
        var level = new ComboBox
        {
            ItemsSource = Levels.Order,
            SelectedItem = e.Level,
            Width = Widths.FieldS
        };
        var kind = e.List == "configs" ? new ComboBox
        {
            ItemsSource = AppFolderProbe.ConfigKinds,
            SelectedItem = e.Kind == "" ? "instructions" : e.Kind,
            Width = Widths.FieldS
        } : null;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = Space.S
        };
        EntryRow? row = null;
        var remove = IconBtn("delete", "Remove", () => Fire.Forget(RemoveAsync()), danger: true);
        async Task RemoveAsync()
        {
            _rows.Remove(row!);
            await Motion.Remove(grid);   // closes up before it goes
            _lists[e.List].Children.Remove(grid);
        }

        grid.Children.Add(path);
        grid.Children.Add(level.With(c => Grid.SetColumn(c, 1)));
        if (kind != null)
        {
            grid.Children.Add(kind.With(c => Grid.SetColumn(c, 2)));
        }

        grid.Children.Add(remove.With(b => Grid.SetColumn(b, 3)));
        row = new EntryRow(e.List, path, level, kind, grid) { Found = found };
        path.PropertyChanged += (_, a) =>
        {
            if (a.Property == TextBox.TextProperty)
            {
                row.Found = false;   // changed by the user: kept when the folder changes
            }
        };
        _rows.Add(row);
        _lists[e.List].Children.Add(grid);
        return row;
    }

    private async Task BrowseAsync()
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "App config folder" });
        if (r.Count > 0 && r[0].TryGetLocalPath() is { } p)
        {
            _folder.Text = p;   // the property change looks into it
        }
    }

    /// <summary>Looks into the folder once typing pauses, off the UI thread; a later look wins over an earlier one still waiting or running.</summary>
    private void Look(string folder) => Fire.Forget(LookAsync(folder));

    private async Task LookAsync(string folder)
    {
        var look = ++_look;
        if (folder.Trim() == "")
        {
            ShowFound([]);
            return;
        }

        _status.Text = "Looking in the folder…";
        await Task.Delay(TypingPause);
        if (look != _look)
        {
            return;   // still typing
        }

        var found = await Task.Run(() => AppFolderProbe.Entries(folder, AppFolderProbe.Look(folder)));
        if (look == _look)
        {
            ShowFound(found);
        }
    }

    /// <summary>Replaces the rows filled in from the last folder with what this one holds; rows the user added or changed stay.</summary>
    private void ShowFound(List<AppEntry> found)
    {
        foreach (var r in _rows.Where(r => r.Found).ToList())
        {
            _rows.Remove(r);
            _lists[r.List].Children.Remove(r.View);
        }

        foreach (var e in found)
        {
            AddEntry(e, true);
        }

        ShowStatus(found.Count);
    }

    private void ShowStatus(int found = 0)
    {
        var folder = (_folder.Text ?? "").Trim();
        _status.Text = folder == "" ? "Detects the app. What it holds is filled in below."
            : found > 0 ? $"Found {found} item{(found == 1 ? "" : "s")}: filled in below."
            : Directory.Exists(PathUtil.Expand(folder)) ? "Nothing known found here. Add rows below."
            : "Folder not found.";
    }

    private void Save()
    {
        var name = (_name.Text ?? "").Trim();
        var folder = (_folder.Text ?? "").Trim();
        if (name == "" || folder == "")
        {
            Refused("Enter the app's name and its config folder.");
            return;
        }

        var def = AppFolderProbe.Build(name, _cat.SelectedItem as string ?? "custom", folder, Entries);
        if (AppCatalog.Refuse(def) is { } why)
        {
            Refused(why);   // a tool AI Stackprint already supports: no second copy of it
            return;
        }

        Close(def);
    }

    private void Refused(string why)
    {
        _refused.Text = why;
        _refused.IsVisible = true;
    }
}
