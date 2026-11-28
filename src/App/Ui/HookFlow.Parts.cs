// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal sealed partial class HookFlow
{
    private static Control Arrow() => Icon("chevron_down", IconSize.S).With(i => i.HorizontalAlignment = HorizontalAlignment.Center);

    /// <summary>The nodes top to bottom, with an arrow between them when <paramref name="arrows"/>.</summary>
    private StackPanel Nodes(FlowNode[] nodes, bool arrows)
    {
        var sp = new StackPanel { Spacing = Space.XS };
        foreach (var n in nodes)
        {
            if (arrows && sp.Children.Count > 0)
            {
                sp.Children.Add(Arrow());
            }

            sp.Children.Add(n switch
            {
                FlowStep s => Step(s),
                FlowBox b => Group(b),
                _ => Tool()
            });
        }

        return sp;
    }

    /// <summary>One card on the main path: its events on one line (each with its label when the user has hooks for it); the tooltip says what happens.</summary>
    private Border Step(FlowStep s)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var e in s.Events)
        {
            row.Children.Add(EventLine(e).With(c => c.Margin = new Thickness(0, 0, Space.L, 0)));
        }

        var chip = Card(row);
        ToolTip.SetTip(chip, s.Hint);
        return chip.Themed(Border.BackgroundProperty, Surface).Themed(Border.BorderBrushProperty, BorderKey);
    }

    /// <summary>The event name; with the count when it is in the Event filter list.</summary>
    private Control EventLine(string e)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S };
        row.Children.Add(Txt(char.ToUpperInvariant(e[0]) + e[1..], TextSize.Small));   // apps spell events both ways (stop, Stop): one look, like the table
        if (_mine.TryGetValue(e, out var n))
        {
            row.Children.Add(Badge(n));
        }

        return row;
    }

    /// <summary>The number of the user's hooks that use the event, in the accent color and the emphasis weight.</summary>
    private static TextBlock Badge(int count)
    {
        var number = Txt(count.ToString(System.Globalization.CultureInfo.InvariantCulture), TextSize.Small, true, Accent).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        ToolTip.SetTip(number, count == 1 ? "1 hook of yours uses this event" : $"{count} hooks of yours use this event");
        return number;
    }

    private static Border Card(Control content) => new()
    {
        Padding = new Thickness(Space.M, Space.XS),
        CornerRadius = new CornerRadius(Radius.M),
        BorderThickness = new Thickness(Stroke.Thin),
        Child = content
    };

    /// <summary>Where the tool itself runs: not an event, so no hook.</summary>
    private static Border Tool() => Card(Txt("The tool runs", TextSize.Caption, false, Muted).With(t => t.HorizontalAlignment = HorizontalAlignment.Center))
        .Themed(Border.BorderBrushProperty, BorderKey);

    /// <summary>A labelled box around nodes (a turn, the agentic loop).</summary>
    private Border Group(FlowBox b)
    {
        var color = b.Strong ? Accent : Muted;
        var inner = new StackPanel { Spacing = Space.XS, Children = { Txt(b.Label, TextSize.Caption, true, color), Nodes(b.Nodes, b.Arrows) } };
        return new Border
        {
            Padding = new Thickness(Space.S),
            CornerRadius = new CornerRadius(Radius.M),
            BorderThickness = new Thickness(Stroke.Thin),
            Child = inner
        }.Themed(Border.BorderBrushProperty, color);
    }

    /// <summary>The events outside the main order: one line each, the tooltip says when; the ones in the user's hooks carry the number.</summary>
    private StackPanel AnyTime((string Event, string When)[] other)
    {
        var list = new StackPanel { Spacing = Space.XS, Children = { Txt("Any time", TextSize.Caption, true, Muted) } };
        foreach (var (e, when) in other)
        {
            var card = Card(EventLine(e));
            ToolTip.SetTip(card, when);
            list.Children.Add(card.Themed(Border.BorderBrushProperty, BorderKey));
        }

        return list;
    }
}
