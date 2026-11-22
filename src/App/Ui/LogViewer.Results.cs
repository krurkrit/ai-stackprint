// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The search results panel: the count and x on top, the rows below, built 100 at a time.</summary>
internal partial class LogViewer
{
    /// <summary>The x on the results: hides them (a search still running is stopped); the words stay in the box.</summary>
    private void CloseResults()
    {
        _search?.Cancel();
        _hits.Clear();
        _jumps.Clear();
        _at = -1;
        _count = null;
        _rows = null;
        _results.Content = null;
    }

    /// <summary>The results header: how many, then "3 of 120" once Enter or a click has gone to one.</summary>
    private TextBlock Count(string total) => _count = Txt(total, TextSize.Caption, true).With(t => t.Tag = total);

    private const int RowsAtOnce = 100;   // result rows built at a time: a long list builds the rest when asked
    private readonly List<Func<Control>> _unbuilt = [];   // result rows not built yet, in order
    private Button? _moreRows;

    private Border Panel(Control head, IReadOnlyList<Func<Control>>? rows = null)
    {
        var close = IconBtn("dismiss", "Close the results (the search words stay; change them or an option to search again)", CloseResults);
        DockPanel.SetDock(close, Dock.Right);
        var top = new DockPanel { Margin = new Thickness(Space.XS, 0, 0, Space.S), Children = { close, head } };
        DockPanel.SetDock(top, Dock.Top);   // the count and the x stay where they are while the results scroll
        var sp = _rows = new StackPanel { Spacing = Space.XXS };
        _unbuilt.Clear();
        _unbuilt.AddRange(rows ?? []);
        _moreRows = null;
        BuildRows();

        return new Border
        {
            Width = Widths.Popup,
            CornerRadius = new CornerRadius(Radius.M),
            Padding = new Thickness(Space.S),
            Child = new DockPanel { Children = { top, new ScrollViewer { Content = sp } } }
        }.Themed(Border.BackgroundProperty, Background);
    }

    /// <summary>Builds the next <see cref="RowsAtOnce"/> result rows, with a button for the rest while some are left.</summary>
    private void BuildRows()
    {
        if (_rows is not { } sp)
        {
            return;
        }

        if (_moreRows != null)
        {
            sp.Children.Remove(_moreRows);
            _moreRows = null;
        }

        var n = Math.Min(RowsAtOnce, _unbuilt.Count);
        sp.Children.AddRange(_unbuilt.Take(n).Select(make => make()));
        _unbuilt.RemoveRange(0, n);
        if (_unbuilt.Count > 0)
        {
            _moreRows = Btn($"Show {Math.Min(RowsAtOnce, _unbuilt.Count):N0} more ({_unbuilt.Count:N0} not shown)", BuildRows);
            _moreRows.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            sp.Children.Add(_moreRows);
        }
    }

    /// <summary>Adds a result row at the end (a match in what a followed file added): built now, or after the rows not built yet.</summary>
    private void AddRow(Func<Control> make)
    {
        if (_unbuilt.Count > 0)
        {
            _unbuilt.Add(make);
            _moreRows?.Content = $"Show {Math.Min(RowsAtOnce, _unbuilt.Count):N0} more ({_unbuilt.Count:N0} not shown)";
            return;
        }

        _rows?.Children.Add(make());
    }

    /// <summary>A result row: where (line or time and role), the text with the matches highlighted; a click goes there.</summary>
    private Border Hit(string where, string text, LogQuery q, int index)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(Radius.S),
            Padding = new Thickness(Space.S, Space.XS),
            Cursor = Hand,
            Background = Avalonia.Media.Brushes.Transparent,
            Child = new StackPanel
            {
                Children = { Txt(where, TextSize.Caption, false, Accent).With(t =>
                {
                    t.FontFamily = Theme.Mono;
                    t.FontSize = Theme.MonoSize;
                }),
                Table.Marked(Txt(text, TextSize.Caption), text, q.Spans(text)).With(t =>
                {
                    t.FontFamily = Theme.Mono;
                    t.FontSize = Theme.MonoSize;
                    t.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
                    t.MaxLines = 3;
                }) }
            }
        };
        b.PointerEntered += (_, _) => b.Themed(Border.BackgroundProperty, Subtle);
        b.PointerExited += (_, _) => b.Background = Avalonia.Media.Brushes.Transparent;
        b.PointerReleased += (_, _) => Go(index);
        return b;
    }

}
