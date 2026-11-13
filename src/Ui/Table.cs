using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// A column: header text, a Grid width ("2*", "120"), right alignment for numbers, and a minimum width for a star column
/// (short values like an app name or version would otherwise break mid-word in a narrow window). Use star or pixel widths so rows line up.
/// </summary>
record Col(string Head, string Width, bool Right = false, double Min = 0);

/// <summary>A row: one control per column, optional click handler, and whether it is the current selection.</summary>
record TableRow(Control[] Cells, Action? Click = null, bool Selected = false);

/// <summary>Flat list with column headers, row dividers, hover and selection. Replaces the old expandable tree.</summary>
static class Table
{
    public static Control Build(Col[] cols, IEnumerable<TableRow> rows, string empty = "Nothing found.")
    {
        var widths = string.Join(",", cols.Select(c => c.Width));
        ColumnDefinitions Defs()
        {
            var d = new ColumnDefinitions(widths);
            for (int i = 0; i < cols.Length; i++) d[i].MinWidth = Size(cols[i].Min);
            return d;
        }
        var sp = new StackPanel();
        var head = new Grid { ColumnDefinitions = Defs(), ColumnSpacing = 12, Margin = new Thickness(10, 0, 10, 6) };
        for (int c = 0; c < cols.Length; c++)
            head.Children.Add(Txt(cols[c].Head, 12, true, Muted).With(t =>
            {
                Grid.SetColumn(t, c); if (cols[c].Right) t.HorizontalAlignment = HorizontalAlignment.Right;
                t.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;   // one line; a squeezed column shows "Pa…", not a letter per line
                ToolTip.SetTip(t, cols[c].Head == "" ? null : cols[c].Head);
            }));
        sp.Children.Add(head);

        int n = 0;
        foreach (var r in rows)
        {
            var g = new Grid { ColumnDefinitions = Defs(), ColumnSpacing = 12 };
            for (int c = 0; c < r.Cells.Length; c++)
            {
                r.Cells[c].VerticalAlignment = VerticalAlignment.Center;
                if (c < cols.Length && cols[c].Right) r.Cells[c].HorizontalAlignment = HorizontalAlignment.Right;
                if (c < cols.Length && cols[c].Width.EndsWith('*')) r.Cells[c].ClipToBounds = true;   // a column squeezed by the others' minimums hides its text instead of drawing over them
                Grid.SetColumn(r.Cells[c], c);
                g.Children.Add(r.Cells[c]);
            }
            // Straight divider on the outer border; only the inner highlight is rounded.
            var row = new Border { Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(6), Child = g, Background = Avalonia.Media.Brushes.Transparent };
            sp.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 2), Child = row }.Themed(Border.BorderBrushProperty, BorderKey));
            if (r.Selected) row.Themed(Border.BackgroundProperty, Selected);
            if (r.Click is { } click)
            {
                row.Cursor = Hand;
                if (!r.Selected)
                {
                    row.PointerEntered += (_, _) => row.Themed(Border.BackgroundProperty, Subtle);
                    row.PointerExited += (_, _) => row.Background = Avalonia.Media.Brushes.Transparent;
                }
                row.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left) click(); };
            }
            n++;
        }
        if (n == 0) sp.Children.Add(Txt(empty, 14, false, Muted).With(t => t.Margin = new Thickness(10, 8)));
        return sp;
    }

    /// <summary>Name with a one-line muted description under it.</summary>
    public static Control NameDesc(string name, string desc, bool bold = true)
    {
        var sp = new StackPanel { Spacing = 2, Children = { Txt(name, 14, bold) } };
        if (desc != "") sp.Children.Add(Txt(desc, 12, false, Muted).With(t => { t.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis; }));
        return sp;
    }
}
