// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class ItemBrowser
{
    /// <summary>Opens, switches or closes the panel in place, so the list keeps its scroll position.</summary>
    private void ShowPanel()
    {
        if (_bottom == null || _splitter == null)
        {
            return;
        }

        var shown = ++_shown;
        var opening = _sel != null && !_bottom.IsVisible;
        if (_sel == null && _bottom.IsVisible)
        {
            Fire.Forget(HidePanelAsync(_bottom, shown));   // slides down and fades, then the list takes the room back
            return;
        }

        _bottom.Child = _sel == null ? null : _panel.Build(_sel, Close).Free();   // the panel outlives the page: take it from the old one
        _splitter.IsVisible = _bottom.IsVisible = _sel != null;
        Layout();
        if (opening)
        {
            Motion.Enter(_bottom, Duration.Normal, PanelSlide);   // slides up as it fades in; switching items does not move it
        }
    }

    private const double PanelSlide = Space.XXXL;
    private int _shown;

    private async Task HidePanelAsync(Border bottom, int shown)
    {
        await Motion.Leave(bottom, Duration.Fast, PanelSlide);
        if (shown != _shown || bottom != _bottom || _splitter == null)
        {
            return;   // opened again, or the page was rebuilt, while it faded
        }

        bottom.Child = null;
        _splitter.IsVisible = bottom.IsVisible = false;
        Layout();
    }

    private void Layout()
    {
        if (_grid == null)
        {
            return;
        }

        var open = _sel != null;
        _grid.RowDefinitions[0].Height = open && _max ? new GridLength(0) : GridLength.Star;
        _grid.RowDefinitions[2].Height = !open ? new GridLength(0) : _max ? GridLength.Star : new GridLength(Math.Clamp(_x.S.State.PanelHeight, MinPanel, MaxPanel));
    }

    private void Close()
    {
        _sel = null;
        _max = false;
        ShowPanel();
        Fill();
    }

    private void Pick(Item i)
    {
        _sel = _sel != null && Same(i, _sel) ? null : i;
        if (_sel == null) { _max = false; }
        ShowPanel();
        Fill();
    }

    /// <summary>The same skill or plugin across scans. Records compare lists by reference, so a rescan would make every item look new.</summary>
    public static bool Same(Item a, Item b) => a.Kind == b.Kind && a.App == b.App && a.Level == b.Level && a.Name == b.Name && a.Path == b.Path && a.Project == b.Project;

    private void Fill()
    {
        _wait.Stop();
        _query = SearchQuery.Parse(_q);
        var shown = _filter.Apply([.. _index.Find(_query)], _items);   // best matches first

        // minimums fit the narrowest window (900 px): Path gives way first
        var cols = _kind switch
        {
            "Plugin" => new[] { Cols.Name("Plugin"), Cols.App(), Cols.Level(), Cols.Version(), Cols.Path(), Cols.Actions() },
            "Skill" => [Cols.Name("Skill"), Cols.App(), Cols.Level(), Cols.Type("Invoked by", HeaderHelp.InvokedBy), Cols.Path(), Cols.Actions()],
            _ => [Cols.Name(_kind), Cols.App(), Cols.Level(), Cols.Path(), Cols.Actions()]   // Command, Agent
        };
        _table.Content = Table.Virtual(cols, _filter.RowMakers(shown, Row, cols.Length, Fill),
            _items.Count == 0 ? $"No {LibraryView.Title(_kind).ToLowerInvariant()} found." : "Nothing matches. Clear the search or filters.");
    }

    private TableRow Row(Item i)
    {
        List<Control> cells = [Table.NameDesc(i.Name, i.Description, marks: _query), DetailPanel.AppLink(_x, i.App, i.Source), AppDetailView.LevelCell(i)];
        if (_kind == "Plugin")
        {
            cells.Add(Txt(i.Version ?? "", TextSize.Small));
        }
        else if (_kind == "Skill")
        {
            cells.Add(AppDetailView.InvokeCell(i));
        }

        cells.Add(PathCell(i.Path));
        cells.Add(Actions(i));
        return new([.. cells], () => Pick(i), _sel != null && Same(i, _sel));
    }

    /// <summary>Open folder and copy path at the left of the cell; remove (when allowed) at the right edge, so it always sits in the same place.</summary>
    private Grid Actions(Item i)
    {
        var sp = (StackPanel)W.Row(IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(i.Path)), IconBtn("copy", "Copy path", () => Toast.Copy(_x.Owner, i.Path)));
        sp.Spacing = 0;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        g.Children.Add(sp);
        Button? remove = null;
        if (i.Kind == "Skill" && SkillManager.CannotRemove(i) == null)
        {
            remove = IconBtn("delete", "Remove skill", () => AppActions.RemoveSkill(_x, i));
        }
        else if (i.Kind == "Plugin" && _x.S.Def(i.App) is { PluginCommands: not null } d)
        {
            remove = IconBtn("delete", "Remove plugin", () => AppActions.RemovePlugin(_x, d, i));
        }

        if (remove != null)
        {
            Grid.SetColumn(remove, 2);
            g.Children.Add(remove);
        }

        return g;
    }

    /// <summary>The whole path, wrapped, never cut: the folder part muted, the name part (skill folder + file) in the emphasis weight.</summary>
    public static Control PathCell(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        var parts = path.TrimEnd(sep).Split(sep);
        var keep = parts.Length >= 2 && parts[^1].Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        var head = string.Join(sep, parts[..^keep]) + (parts.Length > keep ? sep.ToString() : "");
        var tb = new TextBlock
        {
            FontFamily = Theme.Mono,
            FontSize = Size(TextSize.Caption),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxLines = 4,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
        }   /* a squeezed column (narrow window) would otherwise wrap a letter per line; the tooltip has the full path */.Themed(TextBlock.ForegroundProperty, Text);
        tb.Inlines =
        [
            new Run(head).With(r => r.Bind(TextElement.ForegroundProperty, tb.GetResourceObservable(Muted))),
            new Run(string.Join(sep, parts[^keep..])) { FontWeight = Emphasis }
        ];
        ToolTip.SetTip(tb, path);
        return tb;
    }
}
