// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The hook events as a flow, top to bottom, in the order they happen: the tool call pair sits in a box that repeats.
/// While the pop-up is open a highlight walks down the steps, so the order is easy to follow; it stops when the pop-up closes.
/// </summary>
internal sealed class HookFlow : StackPanel
{
    private readonly List<Border> _steps = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private int _at;

    public HookFlow()
    {
        Spacing = Space.S;
        Width = Widths.DialogS;
        Children.Add(Title("Hook events, in order", TextSize.Subtitle));
        Children.Add(Step("SessionStart", "A session begins"));
        Children.Add(Arrow());
        Children.Add(Step("UserPromptSubmit", "You send a prompt"));
        Children.Add(Arrow());
        var loop = new StackPanel
        {
            Spacing = Space.S,
            Children = { Step("PreToolUse", "Before a tool runs; can block it"), Arrow(), Step("PostToolUse", "After it ran") }
        };
        Children.Add(new Border
        {
            Padding = new Thickness(Space.M),
            CornerRadius = new CornerRadius(Radius.M),
            BorderThickness = new Thickness(Stroke.Thin),
            Child = new StackPanel { Spacing = Space.S, Children = { Txt("Repeats for every tool call", TextSize.Caption, false, Text), loop } }
        }.Themed(Border.BorderBrushProperty, Text));
        Children.Add(Arrow());
        Children.Add(Step("Stop", "The agent finishes its answer"));
        Children.Add(Arrow());
        Children.Add(Step("SessionEnd", "The session closes"));
        Children.Add(Txt("Any time: Notification, SubagentStop, PreCompact", TextSize.Caption, false, Text));
        Light();
        _timer.Tick += (_, _) =>
        {
            _at = (_at + 1) % _steps.Count;
            Light();
        };
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private Border Step(string name, string hint)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = Space.L };
        row.Children.Add(Txt(name, TextSize.Small, true));
        row.Children.Add(Txt(hint, TextSize.Caption, false, Muted).With(t =>
        {
            Grid.SetColumn(t, 1);
            t.HorizontalAlignment = HorizontalAlignment.Right;
        }));
        var chip = new Border
        {
            Padding = new Thickness(Space.M, Space.S),
            CornerRadius = new CornerRadius(Radius.M),
            BorderThickness = new Thickness(Stroke.Thin),
            Child = row
        };
        _steps.Add(chip);
        return chip;
    }

    private static Control Arrow() => Icon("chevron_down", IconSize.S).With(i => i.HorizontalAlignment = HorizontalAlignment.Center);

    /// <summary>The step the highlight is on: the accent border and the selection fill; the others are plain cards.</summary>
    private void Light()
    {
        for (var i = 0; i < _steps.Count; i++)
        {
            var on = i == _at;
            _steps[i].Themed(Border.BackgroundProperty, on ? Selected : Surface).Themed(Border.BorderBrushProperty, on ? Accent : BorderKey);
        }
    }
}
