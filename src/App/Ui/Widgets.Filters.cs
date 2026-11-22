// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;

namespace AiStackprint.Ui;

/// <summary>
/// A filter as a drop-down button: its menu has a check box per value with a muted count; the set of off values holds the unchecked values (empty = all).
/// While some are unchecked, the button has the accent border (the page lists what is on below its tool bar).
/// </summary>
internal sealed class FilterMenu : Button
{
    private readonly HashSet<string> _off;

    protected override Type StyleKeyOverride => typeof(Button);   // styled as a button

    /// <param name="opts">The values offered now, read each time the menu opens (the counts follow the search and the other filters).</param>
    public FilterMenu(string label, Func<IReadOnlyList<(string Label, string Value, int Count)>> opts, HashSet<string> off, Action changed)
    {
        _off = off;
        // a plain text block takes the button's text color, so it follows the hover color (W.Txt has a fixed one)
        Content = W.Row(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, W.Icon("chevron_down", IconSize.XS).With(i => i.Opacity = .7));   // the button's text color, quieter (it follows the hover colors)
        W.AccentHover(this);   // under the pointer: like an input and a side menu item
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

    /// <summary>The accent border while some of the <paramref name="offered"/> values are off.</summary>
    public void Update(int offered) => this.Themed(BorderBrushProperty, _off.Count > 0 && offered > 0 ? W.Accent : W.BorderKey);
}

internal static partial class W
{
    /// <summary>Pointer over and pressed: exactly the accent (Rescan) button's own colors, as a side menu item and an input show them under the pointer.</summary>
    public static void AccentHover(Button b)
    {
        var app = Application.Current!;
        foreach (var v in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var d = new ResourceDictionary();
            foreach (var s in new[] { "PointerOver", "Pressed" })
            {
                if (app.TryGetResource("AccentButtonBackground" + s, v, out var bg))
                {
                    d["ButtonBackground" + s] = bg;
                    d["ButtonBorderBrush" + s] = bg;
                }

                if (app.TryGetResource("AccentButtonForeground" + s, v, out var fg))
                {
                    d["ButtonForeground" + s] = fg;
                }
            }
            b.Resources.ThemeDictionaries[v] = d;
        }
    }

    /// <summary>Toggles in one rounded group (like the theme switch): each one is on or off on its own; one that is on is filled with the selection color. The same height as an input.</summary>
    public static Control Toggles(IEnumerable<(string Label, string Value)> opts, ISet<string> on, Action<string> toggle) => Segmented(opts, on.Contains, toggle);

    /// <summary>One choice among a few as one rounded group (like the theme switch): the chosen one is filled with the selection color. The same height as an input.</summary>
    public static Control Segmented(IEnumerable<(string Label, string Value)> opts, string current, Action<string> pick) => Segmented(opts, v => v == current, pick);

    private static Border Segmented(IEnumerable<(string Label, string Value)> opts, Func<string, bool> isOn, Action<string> pick)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.XXS };
        foreach (var (label, value) in opts)
        {
            var on = isOn(value);
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

            AccentHover(b);   // under the pointer: like a side menu item
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
