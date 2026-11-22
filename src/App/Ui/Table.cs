// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// A column: header text, a Grid width ("2*", "120"), right alignment for numbers, and a minimum width for a star column
/// (short values like an app name or version would otherwise break mid-word in a narrow window). Use star or pixel widths so rows line up.
/// </summary>
internal record Col(string Head, string Width, bool Right = false, double Min = 0);

/// <summary>
/// Columns that appear on several pages. Order on every page: the main value (name), then from the biggest scope to the smallest (App, Level, Type),
/// then details (Version, Runs, Path ...), then the buttons. A column of the same kind has the same width everywhere.
/// </summary>
internal static class Cols
{
    private static string Px(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static Col Name(string head = "Name", string width = "2*") => new(head, width, Min: 120);
    public static Col App(string head = "App") => new(head, Px(Widths.ColApp));
    public static Col Level() => new("Level", Px(Widths.ColLevel));
    public static Col Type(string head = "Type") => new(head, Px(Widths.ColType));
    public static Col Status() => new("Status", Px(Widths.ColStatus));
    public static Col Version() => new("Version", Px(Widths.ColVersion));
    public static Col Count(string head) => new(head, Px(Widths.ColCount), true);
    public static Col Path() => new("Path", "3*");
    public static Col Action() => new("", Px(Widths.ColAction));
    public static Col Actions() => new("", Px(Widths.ColActions));
}

/// <summary>A row: one control per column, optional click handler, and whether it is the current selection.</summary>
internal record TableRow(Control[] Cells, Action? Click = null, bool Selected = false);

/// <summary>Flat list with column headers, row dividers, hover and selection. Replaces the old expandable tree.</summary>
internal static partial class Table
{
    internal const string SelectedClass = "selected-row";

    /// <summary>Scrolls the page so the selected row of the table under <paramref name="host"/> is in view (after the next layout, when the rows have their places).</summary>
    internal static void RevealSelected(Control host) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(host).OfType<Control>().FirstOrDefault(c => c.Classes.Contains(SelectedClass))?.BringIntoView(),
            Avalonia.Threading.DispatcherPriority.Loaded);

    /// <summary>
    /// A list with more rows than this is built for the part on screen only (<see cref="VirtualRows"/>); a shorter one builds every row.
    /// Set at start from the setting <c>virtualListRows</c> (default/settings.json).
    /// </summary>
    public static int VirtualFrom { get; set; } = 60;

    /// <summary>The narrowest the table can be drawn: fixed columns in full, star columns at their minimum, and the gaps. Below this a page offers a horizontal scroll bar (<see cref="Scrollable"/>).</summary>
    public static double MinWidth(Col[] cols)
    {
        double sum = Space.L * 2 + Space.L * (cols.Length - 1);   // the row's padding and the gaps between columns
        foreach (var c in cols)
        {
            sum += double.TryParse(c.Width, System.Globalization.CultureInfo.InvariantCulture, out var px) ? Size(px) : Size(Math.Max(c.Min, Sizes.StarMin));
        }

        return sum;
    }

    /// <summary>A table that scrolls sideways when the window is narrower than <paramref name="minWidth"/>, instead of cutting off its last columns.</summary>
    public static Control Scrollable(Control table, double minWidth)
    {
        table.MinWidth = minWidth;
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = table
        };
    }

    private static ColumnDefinitions Defs(Col[] cols)
    {
        var d = new ColumnDefinitions();
        for (var i = 0; i < cols.Length; i++)
        {
            var w = cols[i].Width;
            d.Add(new ColumnDefinition(double.TryParse(w, System.Globalization.CultureInfo.InvariantCulture, out var px) ? new GridLength(Size(px)) : GridLength.Parse(w)) { MinWidth = Size(cols[i].Min) });
        }

        return d;
    }

    /// <summary>The column headers.</summary>
    private static Grid Header(Col[] cols)
    {
        var head = new Grid
        {
            ColumnDefinitions = Defs(cols),
            ColumnSpacing = Space.L,
            Margin = new Thickness(Space.L, 0, Space.L, Space.S)
        };
        for (var c = 0; c < cols.Length; c++)
        {
            head.Children.Add(Txt(cols[c].Head, TextSize.Caption, true, Muted).With(t =>
            {
                Grid.SetColumn(t, c);
                if (cols[c].Right)
                {
                    t.HorizontalAlignment = HorizontalAlignment.Right;
                }

                t.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
                t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;   // one line; a squeezed column shows "Pa…", not a letter per line
                ToolTip.SetTip(t, cols[c].Head == "" ? null : cols[c].Head);
            }));
        }

        return head;
    }

    /// <summary>One row: its cells in the columns, a divider above, hover, selection and click.</summary>
    private static Border RowOf(Col[] cols, TableRow r)
    {
        var g = new Grid
        {
            ColumnDefinitions = Defs(cols),
            ColumnSpacing = Space.L
        };
        for (var c = 0; c < r.Cells.Length; c++)
        {
            r.Cells[c].VerticalAlignment = VerticalAlignment.Top;   // every cell from the top of the row
            if (c < cols.Length && cols[c].Right)
            {
                r.Cells[c].HorizontalAlignment = HorizontalAlignment.Right;
            }

            if (c < cols.Length && cols[c].Width.EndsWith('*'))
            {
                r.Cells[c].ClipToBounds = true;   // a column squeezed by the others' minimums hides its text instead of drawing over them
            }

            Grid.SetColumn(r.Cells[c], c);
            g.Children.Add(r.Cells[c]);
        }
        // Straight divider on the outer border; only the inner highlight is rounded.
        var row = new Border
        {
            Padding = new Thickness(Space.L, Space.M),
            CornerRadius = new CornerRadius(Radius.M),
            Child = g,
            Background = Avalonia.Media.Brushes.Transparent
        };
        var divider = new Border
        {
            BorderThickness = new Thickness(0, Space.Hair, 0, 0),
            Padding = new Thickness(0, Space.XXS),
            Child = row
        }.Themed(Border.BorderBrushProperty, BorderKey);
        if (r.Selected)
        {
            row.Themed(Border.BackgroundProperty, Selected);
            divider.Classes.Add(SelectedClass);   // a page can scroll to the selected row
        }

        if (r.Click is { } click)
        {
            row.Cursor = Hand;
            if (!r.Selected)
            {
                row.PointerEntered += (_, _) => row.Themed(Border.BackgroundProperty, Subtle);
                row.PointerExited += (_, _) => row.Background = Avalonia.Media.Brushes.Transparent;
            }
            row.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left) { click(); } };
        }

        return divider;
    }

    /// <summary>
    /// A long table whose rows are built only for the part on screen (<see cref="VirtualRows"/>): <paramref name="rows"/> make a row when it comes near, so hundreds of rows cost
    /// a screenful of controls. Needs the page's scroll viewer above it; the header stays with the table, not with the screen.
    /// </summary>
    public static Control Virtual(Col[] cols, IReadOnlyList<Func<TableRow>> rows, string empty = "Nothing found.")
    {
        if (rows.Count <= VirtualFrom)
        {
            return Build(cols, rows.Select(m => m()), empty);   // a short list: every row at once, nothing to manage
        }

        var sp = new StackPanel();
        sp.Children.Add(Header(cols));
        sp.Children.Add(rows.Count == 0
            ? Txt(empty, TextSize.Body, false, Muted).With(t => t.Margin = new Thickness(Space.L, Space.M))
            : new VirtualRows(rows.Count, i => RowOf(cols, rows[i]())));
        return sp;
    }
}
