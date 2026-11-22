// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The Apps page's tool bar, one row: the search, the Status and Category menus, Group by (Category and Status toggles) and Reset at the end (dimmed while nothing is filtered).
/// It is built once per visit, so a menu stays open while its check boxes change the list.
/// </summary>
internal partial class AppsView
{
    private const string ByCategory = "category", ByStatus = "status";
    private readonly HashSet<string> _group = [ByCategory];   // Group by: each one on or off; both on groups by both, none on is a plain list
    private FilterMenu? _statusMenu, _catMenu;
    private Button? _reset;
    private readonly ContentControl _groupHost = new();
    private List<(string Label, string Value, int Count)> _statusOpts = [], _catOpts = [];

    private bool ByCat => _group.Contains(ByCategory);
    private bool ByStat => _group.Contains(ByStatus);

    private Control Toolbar()
    {
        _statusMenu = new FilterMenu("Status", () => _statusOpts, _offStatus, Fill);
        _catMenu = new FilterMenu("Category", () => _catOpts, _offCats, Fill);
        _reset = IconBtn("arrow_reset", "Reset the search and filters", () =>
        {
            _offStatus.Clear();
            _offCats.Clear();
            _q = "";
            _search.Text = "";
            Fill();
        });
        _reset.Resources["ButtonBackgroundDisabled"] = Avalonia.Media.Brushes.Transparent;   // dimmed: a faded icon, no gray fill
        _reset.Resources["ButtonBorderBrushDisabled"] = Avalonia.Media.Brushes.Transparent;
        _reset.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsEnabledProperty && _reset.Content is Control c)
            {
                c.Opacity = _reset.IsEnabled ? 1 : .35;
            }
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.M,
            Margin = new Avalonia.Thickness(Space.M, 0, 0, 0),
            Children = { _statusMenu, _catMenu, Txt("Group by", TextSize.Small, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center), _groupHost.Free(), _reset }
        };
        DockPanel.SetDock(right, Dock.Right);
        _search.MinWidth = Widths.FieldM;
        return new DockPanel { Children = { right, _search.Free() } };
    }

    /// <summary>The menus' values and counts, their labels, Group by and Reset, after the list changed.</summary>
    private void UpdateToolbar(List<(string, string, int)> statuses, List<(string, string, int)> cats)
    {
        _statusOpts = statuses;
        _catOpts = cats;
        if (_statusMenu is { } s)
        {
            s.IsVisible = statuses.Count > 1;
            s.Update(statuses.Count);
        }

        if (_catMenu is { } c)
        {
            c.IsVisible = cats.Count > 1;
            c.Update(cats.Count);
        }

        _groupHost.Content = Toggles([("Category", ByCategory), ("Status", ByStatus)], _group, g =>
        {
            if (!_group.Remove(g))
            {
                _group.Add(g);
            }

            Fill();
        });
        _reset?.IsEnabled = _q != "" || _offStatus.Count > 0 || _offCats.Count > 0;   // always at the end of the row; dimmed with nothing to reset
    }
}
