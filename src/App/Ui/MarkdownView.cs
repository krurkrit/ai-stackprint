// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Renders chat Markdown (see <see cref="MarkdownLite"/>) as selectable text. <see cref="Render"/> leaves code fences to the caller; <see cref="Document"/> handles them.</summary>
internal static class MarkdownView
{
    /// <param name="link">When set, links are clickable and call it with their target; otherwise they show the target after the text.</param>
    public static Control Render(string md, Action<string>? link = null)
    {
        var sp = new StackPanel { Spacing = 6 };
        foreach (var b in MarkdownLite.Blocks(md))
        {
            sp.Children.Add(Block(b, link));
        }

        return sp;
    }

    /// <summary>A whole Markdown file: prose, plus fenced code highlighted by its language.</summary>
    public static Control Document(string md, Action<string>? link = null)
    {
        var sp = new StackPanel { Spacing = 8 };
        foreach (var (text, lang) in ReadableLog.Fences(md))
        {
            sp.Children.Add(lang == null ? Render(text, link) : ReadableLog.Code(text, lang));
        }

        return sp;
    }

    private static Control Block(MdBlock b, Action<string>? link)
    {
        switch (b.Kind)
        {
            case MdKind.Heading:
                return Rich(b.Text, b.Level switch { 1 => 20, 2 => 17, 3 => 15, _ => 14 }, Heading, link).With(t => t.Margin = new Thickness(0, 4, 0, 0));
            case MdKind.Bullet:
            case MdKind.Number:
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Margin = new Thickness(8 + 18 * b.Level, 0, 0, 0) };
                var mark = Txt(b.Kind == MdKind.Bullet ? "•" : b.Num + ".", 14, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Top);
                var body = Rich(b.Text, 14, Body, link); Grid.SetColumn(body, 1);
                g.Children.Add(mark); g.Children.Add(body);
                return g;
            case MdKind.Quote:
                return new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0), Margin = new Thickness(4, 0, 0, 0), Child = Rich(b.Text, 14, Body, link) }
                    .Themed(Border.BorderBrushProperty, BorderKey);
            case MdKind.Rule:
                return new Border { Height = 1, Margin = new Thickness(0, 4) }.Themed(Border.BackgroundProperty, BorderKey);
            case MdKind.Table when b.Rows is { Count: > 0 } rows:
                var n = rows.Max(r => r.Count);
                var cols = Enumerable.Range(0, n).Select(i => new Col(i < rows[0].Count ? Strip(rows[0][i]) : "", "1*")).ToArray();
                return Table.Build(cols, rows.Skip(1).Select(r => new TableRow([.. Enumerable.Range(0, n).Select(i => (Control)Rich(i < r.Count ? r[i] : "", 13, Body, link))])));
            default:
                return Rich(b.Text, 14, Body, link);
        }
    }

    private static string Strip(string s) => string.Concat(MarkdownLite.Spans(s).Select(x => x.Text));

    /// <summary>Selectable block with bold / italic / code / link runs. Built when shown, so link color follows the theme.</summary>
    private static SelectableTextBlock Rich(string md, double size, FontWeight weight, Action<string>? onLink)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = Size(size), FontWeight = weight }.Themed(TextBlock.ForegroundProperty, Text);
        var built = false;
        tb.AttachedToVisualTree += (_, _) =>
        {
            if (built)
            {
                return;
            }

            built = true;
            var link = tb.TryFindResource(Link, tb.ActualThemeVariant, out var o) ? o as IBrush : null;
            tb.Inlines ??= [];
            var links = new List<(int Start, int Length, string Url)>();
            var at = 0;
            foreach (var s in MarkdownLite.Spans(md))
            {
                // Clickable links show their label only; the target is in the tooltip
                var text = s.Url != null && onLink != null && s.Text.EndsWith($" ({s.Url})") ? s.Text[..^(s.Url.Length + 3)] : s.Text;
                var run = new Run(text);
                if (s.Bold)
                {
                    run.FontWeight = FontWeight.Bold;
                }

                if (s.Italic)
                {
                    run.FontStyle = FontStyle.Italic;
                }

                if (s.Code) { run.FontFamily = Theme.Mono; run.FontSize = Theme.MonoSize; }
                if (s.Url != null) { run.TextDecorations = TextDecorations.Underline; if (link != null) { run.Foreground = link; } links.Add((at, text.Length, s.Url)); }
                tb.Inlines.Add(run);
                at += text.Length;
            }
            if (onLink != null && links.Count > 0)
            {
                Clickable(tb, links, onLink);
            }
        };
        return tb;
    }

    /// <summary>
    /// Links inside selectable text: the text block takes the mouse for selecting, so a click is found from the character under the pointer.
    /// A drag that selects text does not follow the link.
    /// </summary>
    private static void Clickable(SelectableTextBlock tb, List<(int Start, int Length, string Url)> links, Action<string> onLink)
    {
        string? At(Avalonia.Input.PointerEventArgs e)
        {
            var p = e.GetPosition(tb) - new Point(tb.Padding.Left, tb.Padding.Top);
            var hit = tb.TextLayout.HitTestPoint(p);
            if (!hit.IsInside)
            {
                return null;
            }

            var i = hit.TextPosition;
            return links.FirstOrDefault(l => i >= l.Start && i < l.Start + l.Length).Url;
        }
        tb.PointerMoved += (_, e) =>
        {
            var url = At(e);
            tb.Cursor = url != null ? Hand : null;
            ToolTip.SetTip(tb, url);
        };
        tb.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left || tb.SelectionStart != tb.SelectionEnd)
            {
                return;
            }

            if (At(e) is { } url) { e.Handled = true; onLink(url); }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }
}
