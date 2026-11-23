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

        _bottom.Child = _sel == null ? null : _panel.Build(_sel, Close).Free();   // the panel outlives the page: take it from the old one
        _splitter.IsVisible = _bottom.IsVisible = _sel != null;
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

        var cols = _kind == "Plugin"
            // minimums fit the narrowest window (900 px): Path gives way first
            ? new[] { Cols.Name("Plugin"), Cols.App(), Cols.Level(), Cols.Version(), Cols.Path(), Cols.Actions() }
            : [Cols.Name(_kind == "Part" ? "Name" : "Skill"), Cols.App(), Cols.Level(), _kind == "Part" ? Cols.Type() : Cols.Type("Invoked by", HeaderHelp.InvokedBy), Cols.Path(), Cols.Actions()];
        _table.Content = Table.Virtual(cols, _filter.RowMakers(shown, Row, cols.Length, Fill),
            _items.Count == 0 ? $"No {_kind switch { "Plugin" => "plugins", "Part" => "commands or agents", _ => "skills" }} found." : "Nothing matches. Clear the search or filters.");
    }

    private TableRow Row(Item i) => new(
    [
        Table.NameDesc(i.Name, i.Description, marks: _query),
        DetailPanel.AppLink(_x, i.App, i.Source),
        AppDetailView.LevelCell(i),
        _kind switch { "Plugin" => Txt(i.Version ?? "", TextSize.Small), "Part" => Txt(i.Kind, TextSize.Small), _ => AppDetailView.InvokeCell(i) },
        PathCell(i.Path),
        Actions(i)
    ], () => Pick(i), _sel != null && Same(i, _sel));

    private StackPanel Actions(Item i)
    {
        var sp = (StackPanel)W.Row(IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(i.Path)), IconBtn("copy", "Copy path", () => Toast.Copy(_x.Owner, i.Path)));
        sp.Spacing = 0;
        if (i.Kind == "Skill" && SkillManager.CannotRemove(i) == null)
        {
            sp.Children.Add(IconBtn("delete", "Remove skill", () => AppActions.RemoveSkill(_x, i)));
        }
        else if (i.Kind == "Plugin" && _x.S.Def(i.App) is { PluginCommands: not null } d)
        {
            sp.Children.Add(IconBtn("delete", "Remove plugin", () => AppActions.RemovePlugin(_x, d, i)));
        }

        return sp;
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
