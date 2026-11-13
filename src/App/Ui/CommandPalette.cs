// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Ctrl+K: type to find a page, app, project, skill or plugin, or an action (Rescan, Add project…). ↑↓ choose, Enter opens, Esc closes.
/// Drawn over the window; clicking outside closes it.
/// </summary>
internal class CommandPalette(Ctx x, Panel layer, Action<string> go)
{
    private record Entry(string Icon, string Title, string Sub, Action Run);

    private readonly TextBox _box = SearchBox("Go to an app, skill, plugin, project or action", 44);
    private readonly StackPanel _list = new() { Spacing = 2 };
    private List<Entry> _all = [], _shown = [];
    private int _pick;
    private bool _wired;
    public bool IsOpen => layer.Children.Count > 0;

    public void Open()
    {
        if (IsOpen) { _box.Focus(); return; }
        if (!_wired)
        {
            _wired = true;
            _box.TextChanged += (_, _) => { _pick = 0; Filter(); };
            _box.KeyDown += Keys;
        }
        _all = Entries();
        _box.Text = "";
        Filter();
        var card = new Border
        {
            Width = 640,
            MaxHeight = 520,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 72, 0, 0),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = new DockPanel { Children = { _box.Free().With(b => { DockPanel.SetDock(b, Dock.Top); b.Margin = new Thickness(0, 0, 0, 10); }), new ScrollViewer { Content = _list.Free() } } }
        }.Themed(Border.BackgroundProperty, Surface).Themed(Border.BorderBrushProperty, BorderKey);
        card.PointerPressed += (_, e) => e.Handled = true;   // clicks inside do not close it
        var dim = new Border { Child = card }.Themed(Border.BackgroundProperty, ScrimKey);
        dim.PointerPressed += (_, _) => Close();
        layer.Children.Add(dim);
        _box.Focus();
    }

    public void Close() => layer.Children.Clear();

    private List<Entry> Entries()
    {
        var e = new List<Entry>
        {
            new("board", "Dashboard", "Page", () => go("Dashboard")), new("apps", "AI apps", "Page", () => go("Apps")),
            new("puzzle_piece", "Plugins", "Page", () => go("Plugin")), new("sparkle", "Skills", "Page", () => go("Skill")),
            new("document_text", "Logs", "Page", () => go("Logs")), new("warning", "Health", "Page", () => go("Health")), new("settings", "Settings", "Page", () => go("Settings")),
            new("arrow_sync", "Rescan", "Action", x.Rescan), new("folder_add", "Add project folder", "Action", x.AddProject), new("add", "Add app", "Action", x.AddApp),
        };
        foreach (var a in x.ShownApps.OrderBy(a => a.Name))
        {
            e.Add(new(Icons.ForCategory(a.Category), a.Name, a.Installed ? "App" : "App · not found", () => go("App:" + a.Id)));
        }

        foreach (var d in x.S.Defs.Where(d => d.Logs.Count > 0 && x.ShownApps.Any(a => a.Id == d.Id)))
        {
            e.Add(new("document_text", d.Name + " logs", "Logs", () => go("Logs:" + d.Id)));
        }

        foreach (var p in x.S.Projects)
        {
            e.Add(new("folder", Ctx.ProjectName(p), "Project · " + p, () => go("Project:" + p)));
        }

        foreach (var i in x.S.Items.Where(i => i.Kind is "Skill" or "Plugin").OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            e.Add(new(i.Kind == "Skill" ? "sparkle" : "puzzle_piece", i.Name, $"{i.Kind} · {i.Source} · {i.Level}", () => x.Open(i)));
        }

        return e;
    }

    private void Filter()
    {
        var q = (_box.Text ?? "").Trim();
        _shown = [.. (q == "" ? _all.Take(12) : _all.Where(e => e.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Sub.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Title.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : e.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(e => e.Sub == "Page" || e.Sub == "Action" ? 0 : 1).Take(40))];
        _pick = Math.Clamp(_pick, 0, Math.Max(0, _shown.Count - 1));
        _list.Children.Clear();
        for (var i = 0; i < _shown.Count; i++)
        {
            var en = _shown[i]; var at = i;
            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6),
                Cursor = Hand,
                Background = Brushes.Transparent,
                Child = Row(Icon(en.Icon, 16).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Muted), Txt(en.Title, 14, true), Txt(en.Sub, 12, false, Muted))
            };
            if (i == _pick)
            {
                row.Themed(Border.BackgroundProperty, Selected);
            }

            row.PointerReleased += (_, _) => { _pick = at; Run(); };
            _list.Children.Add(row);
        }
        if (_shown.Count == 0)
        {
            _list.Children.Add(Txt("Nothing found.", 13, false, Muted).With(t => t.Margin = new Thickness(10, 6)));
        }
    }

    private void Keys(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Down) { _pick = Math.Min(_pick + 1, _shown.Count - 1); Filter(); e.Handled = true; }
        else if (e.Key == Key.Up) { _pick = Math.Max(_pick - 1, 0); Filter(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Run(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private void Run()
    {
        if (_pick < 0 || _pick >= _shown.Count)
        {
            return;
        }

        var en = _shown[_pick];
        Close();
        en.Run();
    }
}
