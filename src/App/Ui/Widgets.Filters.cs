// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;

namespace AiStackprint.Ui;

/// <summary>
/// A filter as a drop-down button: its menu has a check box per value with a muted count; the set of off values holds the unchecked values (empty = all).
/// While some are unchecked, the button says how many are on ("Category (2)") and has the accent border.
/// </summary>
internal sealed class FilterMenu : Button
{
    private readonly string _label;
    private readonly HashSet<string> _off;
    private readonly TextBlock _text;

    protected override Type StyleKeyOverride => typeof(Button);   // styled as a button

    /// <param name="opts">The values offered now, read each time the menu opens (the counts follow the search and the other filters).</param>
    public FilterMenu(string label, Func<IReadOnlyList<(string Label, string Value, int Count)>> opts, HashSet<string> off, Action changed)
    {
        _label = label;
        _off = off;
        _text = W.Txt(label, TextSize.Body).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        Content = W.Row(_text, W.Icon("chevron_down", IconSize.XS).Themed(TextElement.ForegroundProperty, W.Muted));
        MinHeight = AiStackprint.Ui.Theme.InputHeight;   // (Button.Theme is the control theme)
        Padding = new Thickness(Space.L, 0);
        VerticalContentAlignment = VerticalAlignment.Center;
        var list = new StackPanel { Spacing = Space.XS, Margin = new Thickness(Space.S) };
        var menu = new Flyout { Content = list, Placement = PlacementMode.BottomEdgeAlignedLeft };
        Flyout = menu;
        menu.Opening += (_, _) =>
        {
            var now = opts();
            list.Children.Clear();
            foreach (var (text, value, count) in now)
            {
                var cb = new CheckBox { Content = W.Counted(W.Txt(text, TextSize.Body), count), IsChecked = !_off.Contains(value) };
                cb.IsCheckedChanged += (_, _) =>
                {
                    if (cb.IsChecked == true)
                    {
                        _off.Remove(value);
                    }
                    else if (now.Count(o => !_off.Contains(o.Value)) <= 1)
                    {
                        cb.IsChecked = true;   // the last one on stays on: none would hide everything
                        return;
                    }
                    else
                    {
                        _off.Add(value);
                    }

                    changed();
                };
                list.Children.Add(cb);
            }
        };
        Update(0);
    }

    /// <summary>Shows how many values are on, out of <paramref name="offered"/>, while some are off.</summary>
    public void Update(int offered)
    {
        var filtered = _off.Count > 0 && offered > 0;
        _text.Text = filtered ? $"{_label} ({offered - _off.Count})" : _label;
        this.Themed(BorderBrushProperty, filtered ? W.Accent : W.BorderKey);
    }
}

internal static partial class W
{
    /// <summary>One choice among a few as one rounded group (like the theme switch): the chosen one is filled with the selection color. The same height as an input.</summary>
    public static Control Segmented(IEnumerable<(string Label, string Value)> opts, string current, Action<string> pick)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.XXS };
        foreach (var (label, value) in opts)
        {
            var on = value == current;
            var b = new Button
            {
                Content = label,
                MinHeight = Theme.InputHeight - (2 * (Space.XXS + Stroke.Thin)),
                Padding = new Thickness(Space.L, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Background = Avalonia.Media.Brushes.Transparent
            };
            if (on)
            {
                b.Themed(Button.BackgroundProperty, Selected).Themed(Button.ForegroundProperty, Accent);
            }

            Avalonia.Automation.AutomationProperties.SetName(b, label + (on ? " (on)" : ""));
            b.Click += (_, _) => pick(value);
            group.Children.Add(b);
        }

        return new Border
        {
            Child = group,
            Padding = new Thickness(Space.XXS),
            CornerRadius = new CornerRadius(Radius.M + Space.XXS),
            BorderThickness = new Thickness(Stroke.Thin)
        }
            .Themed(Border.BorderBrushProperty, BorderKey);
    }
}
