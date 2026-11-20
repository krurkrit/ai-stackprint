// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class Table
{
    /// <param name="batch">
    /// 0: every row is built now. Above 0 (a long list, e.g. 250 models): the first <paramref name="batch"/> rows are built now and the rest, batch by batch, after the page is on screen
    /// (the rows are made as they are asked for, so the cost is spread out); it stops if the table leaves the screen.
    /// </param>
    public static Control Build(Col[] cols, IEnumerable<TableRow> rows, string empty = "Nothing found.", int batch = 0)
    {
        var sp = new StackPanel();
        sp.Children.Add(Header(cols));

        var n = 0;
        void AddRow(TableRow r)
        {
            sp.Children.Add(RowOf(cols, r));
            n++;
        }

        var all = rows.GetEnumerator();
        bool Take(int count)   // adds up to count rows; false when there are no more
        {
            for (var i = 0; i < count; i++)
            {
                if (!all.MoveNext())
                {
                    all.Dispose();
                    return false;
                }

                AddRow(all.Current);
            }

            return true;
        }

        if (Take(batch > 0 ? batch : int.MaxValue))
        {
            var started = false;
            void Continue()
            {
                if (TopLevel.GetTopLevel(sp) == null)
                {
                    all.Dispose();   // the page was left: no more rows
                    return;
                }

                if (Take(batch))
                {
                    Dispatcher.UIThread.Post(Continue, DispatcherPriority.Background);
                }
            }

            sp.AttachedToVisualTree += (_, _) =>
            {
                if (!started)
                {
                    started = true;
                    Dispatcher.UIThread.Post(Continue, DispatcherPriority.Background);
                }
            };
        }

        if (n == 0)
        {
            sp.Children.Add(Txt(empty, TextSize.Body, false, Muted).With(t => t.Margin = new Thickness(Space.L, Space.M)));
        }

        return sp;
    }

    /// <summary>Name with a one-line muted description under it; words of <paramref name="marks"/> (a search) are highlighted in both.</summary>
    public static Control NameDesc(string name, string desc, bool bold = true, SearchQuery? marks = null)
    {
        var sp = new StackPanel
        {
            Spacing = Space.XXS,
            Children = { Marked(Txt(name, TextSize.Body, bold), name, marks?.Marks("name")) }
        };
        if (desc != "")
        {
            sp.Children.Add(Marked(Txt(desc, TextSize.Caption, false, Muted), desc, marks?.Marks("desc"))
                .With(t =>
                {
                    t.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
                    t.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
                }));
        }

        return sp;
    }

    /// <summary>The text with each occurrence of <paramref name="words"/> on the highlight color; the plain text when nothing matches.</summary>
    public static TextBlock Marked(TextBlock tb, string text, IReadOnlyList<string>? words) =>
        Marked(tb, text, words is { Count: > 0 } ? SearchQuery.Spans(text, words) : []);

    /// <summary>The text with the <paramref name="spans"/> (start, length; in order, not overlapping) on the highlight color.</summary>
    public static TextBlock Marked(TextBlock tb, string text, IReadOnlyList<(int Start, int Length)> spans)
    {
        if (spans.Count == 0)
        {
            return tb;
        }

        var inlines = new InlineCollection();
        var at = 0;
        foreach (var (start, length) in spans)
        {
            if (start > at)
            {
                inlines.Add(new Run(text[at..start]));
            }

            inlines.Add(new Run(text.Substring(start, length)).With(r => r.Bind(TextElement.BackgroundProperty, tb.GetResourceObservable(HighlightKey))));
            at = start + length;
        }

        if (at < text.Length)
        {
            inlines.Add(new Run(text[at..]));
        }

        tb.Inlines = inlines;
        return tb;
    }
}
