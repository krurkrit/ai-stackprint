// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class Dialogs
{
    /// <summary>Two sessions side by side: the figures that differ (the better one in green), then their steps lined up. A line opens its step; unmatched steps are tinted and the other side has a gap.</summary>
    public static Task Compare(Window owner, string titleA, Session a, string titleB, Session b)
    {
        Window? w = null;
        var card = Math.Min(Widths.DialogXL, Math.Max(Widths.DialogL, owner.Bounds.Width - Widths.DialogMargin));
        var body = CompareBody(titleA, a, titleB, b);
        w = new Window();
        Chrome(w, "Compare sessions", body, card, Btn("Close", () => w.Close(false), true));
        return w.ShowDialog(owner);
    }

    /// <summary>The dialog's content: a note, the summary and the lined-up steps.</summary>
    internal static StackPanel CompareBody(string titleA, Session a, string titleB, Session b) =>
        new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                Txt("Steps are matched by who did what, in order: an approximate match. Click a line for the full step.", TextSize.Small, false, Muted),
                CompareSummary(titleA, titleB, SessionCompare.Summary(a, b)),
                new ScrollViewer
                {
                    MaxHeight = Heights.ViewerL,
                    Content = ComparePairs(SessionCompare.Align(a, b))
                }
            }
        };

    private static Grid CompareSummary(string titleA, string titleB, List<CompareRow> rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Widths.LabelL},*,*") };
        void Cell(int row, int col, Control c)
        {
            c.Margin = new Thickness(0, 0, Space.L, Space.XS);
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            grid.Children.Add(c);
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell(0, 1, Txt(titleA, TextSize.Small, true).With(t => t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis));
        Cell(0, 2, Txt(titleB, TextSize.Small, true).With(t => t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis));
        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var r = rows[i];
            Cell(i + 1, 0, Txt(r.Label, TextSize.Small, false, Muted));
            Cell(i + 1, 1, Txt(r.A, TextSize.Small, r.Better < 0, r.Better < 0 ? SuccessText : Text));
            Cell(i + 1, 2, Txt(r.B, TextSize.Small, r.Better > 0, r.Better > 0 ? SuccessText : Text));
        }

        return grid;
    }

    private static StackPanel ComparePairs(List<StepPair> pairs)
    {
        var list = new StackPanel();
        foreach (var p in pairs)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            var left = CompareCell(p.A, p.Matched);
            var right = CompareCell(p.B, p.Matched);
            Grid.SetColumn(right, 1);
            row.Children.Add(left);
            row.Children.Add(right);
            list.Children.Add(row);
        }

        return list;
    }

    /// <summary>A step as its one-line row, a sub-agent as a block line, a missing one as a gap; a step with no match is tinted.</summary>
    private static Control CompareCell(CompareItem? item, bool matched)
    {
        if (item == null)
        {
            return Txt("", TextSize.Small).With(t => t.MinHeight = Heights.LogRow);
        }

        Control cell = item.Step != null
            ? ReadableLog.CompactRow(item.Step)
            : new Border
            {
                MinHeight = Heights.LogRow,
                Padding = new Thickness(Space.L, Space.XS),
                BorderThickness = new Thickness(Stroke.Medium, 0, 0, Stroke.Thin),
                Child = Row(Txt(item.Label, TextSize.Body, true).With(t => t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis),
                    Txt($"{ReadableLog.Plural(item.Steps, "step")}{(item.Cost > 0 ? " · " + ReadableLog.StepMoney(item.Cost) : "")}", TextSize.Caption, false, Muted))
            }.Themed(Border.BorderBrushProperty, Accent);
        if (!matched && cell is Border b)
        {
            b.Themed(Border.BackgroundProperty, HighlightKey);
        }

        return cell;
    }
}
