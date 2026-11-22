// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace AiStackprint.Ui;

internal static partial class W
{
    public static Button Btn(string text, Action a, bool primary = false, string? icon = null)
    {
        object content = icon == null ? text : Row(Icon(icon, IconSize.S), new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        });
        var b = new Button
        {
            Content = content,
            MinHeight = Theme.InputHeight,
            Padding = new Thickness(Space.XL, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (primary)
        {
            b.Classes.Add("accent");
        }

        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Button for destructive actions (error colors).</summary>
    public static Button DangerBtn(string text, Action a, string? icon = null) =>
        DangerHover(Btn(text, a, false, icon).Themed(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, ErrorFill).Themed(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, OnError), solid: true);

    /// <summary>Small transparent icon-only button with a tooltip. A delete button (or one made with <paramref name="danger"/>, such as a clear or close button) turns the danger red under the pointer.</summary>
    public static Button IconBtn(string icon, string tip, Action a, string key = Muted, bool danger = false)
    {
        danger |= icon == "delete";
        // A danger button's icon takes the button's foreground, so the red hover can change it; the others color the icon itself
        var glyph = danger ? Icon(icon, IconSize.S) : Icon(icon, IconSize.S).Themed(TextElement.ForegroundProperty, key);
        var b = new Button
        {
            Content = glyph,
            Padding = new Thickness(Space.M),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (danger)
        {
            b.Themed(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, key);
            DangerHover(b);
        }

        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>The red of the danger button (error fill, on-error icon) under the pointer and while pressed, like the Windows close button.</summary>
    public static Button DangerHover(Button b, bool solid = false)
    {
        foreach (var v in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
        {
            if (Application.Current!.Resources.ThemeDictionaries.TryGetValue(v, out var td) && td is ResourceDictionary rd && rd.TryGetValue(ErrorFill, out var red) && rd.TryGetValue(OnError, out var onRed))
            {
                b.Resources.ThemeDictionaries[v] = new ResourceDictionary
                {
                    ["ButtonBackgroundPointerOver"] = solid && rd.TryGetValue(ErrorHover, out var h) ? h : red,
                    ["ButtonBackgroundPressed"] = solid && rd.TryGetValue(ErrorPressed, out var pr) ? pr : red,
                    ["ButtonForegroundPointerOver"] = onRed,
                    ["ButtonForegroundPressed"] = onRed
                };
            }
        }

        return b;
    }

    /// <summary>Pill-shaped filter buttons; the selected one uses the accent style.</summary>
    public static Control Pills(IEnumerable<(string label, string value)> opts, string current, Action<string> pick)
    {
        // Spacing on the panel, not a margin on each button: one line is exactly 32 px, so a label beside it lines up
        var wrap = new WrapPanel
        {
            ItemSpacing = Space.S,
            LineSpacing = Space.S
        };
        foreach (var (label, value) in opts)
        {
            // Even padding with the text centered: Fluent's button padding is 1 px larger at the bottom, which put the label above the middle
            var b = new Button
            {
                Content = label,
                MinHeight = Sizes.CompactControl,
                CornerRadius = new CornerRadius(Sizes.PillCorner),
                Padding = new Thickness(Space.L, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            if (value == current)
            {
                b.Classes.Add("accent");
            }

            b.Themed(Button.BorderBrushProperty, value == current ? Accent : BorderKey);   // a solid line: the theme's own border is a gradient that reads as uneven on a pill

            b.Click += (_, _) => pick(value);
            wrap.Children.Add(b);
        }
        return wrap;
    }

    public static Control Row(params Control[] c)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.M
        };
        foreach (var x in c)
        {
            if (!x.IsSet(Layoutable.VerticalAlignmentProperty))
            {
                x.VerticalAlignment = VerticalAlignment.Center;   // a label lines up with the control beside it
            }
        }

        sp.Children.AddRange(c);
        return sp;
    }

    /// <summary>A filter row: muted label in a fixed-width column, so the controls of stacked filter rows line up.</summary>
    /// <summary>Label, then the options; options wrap onto more lines in a narrow window instead of running off the edge.</summary>
    public static Control FilterRow(string label, params Control[] c)
    {
        var name = Txt(label, TextSize.Small, false, Muted).With(t =>
        {
            t.Width = Widths.LabelXS;
            t.VerticalAlignment = VerticalAlignment.Center;
        });
        DockPanel.SetDock(name, Dock.Left);
        var opts = new WrapPanel
        {
            ItemSpacing = Space.M,
            LineSpacing = Space.XS,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var x in c)
        {
            x.VerticalAlignment = VerticalAlignment.Center;
            opts.Children.Add(x);
        }
        return new DockPanel { Children = { name, opts } };
    }

    /// <summary>Drops unchecked values no longer offered; with fewer than two offered, or all of them unchecked, none (nothing left to undo it).</summary>
    public static void KeepOffered(HashSet<string> off, ICollection<string> offered)
    {
        off.IntersectWith(offered);
        if (offered.Count < 2 || off.Count >= offered.Count)
        {
            off.Clear();
        }
    }

    /// <summary>Check box text with a muted count after it.</summary>
    public static Control Counted(Control label, int n) => Row(label, Txt(n.ToString("N0"), TextSize.Small, false, Muted));

    /// <summary>Search field with an icon; the same height as other inputs (<see cref="Theme.InputHeight"/>) unless <paramref name="minHeight"/> asks for more.</summary>
    public static TextBox SearchBox(string placeholder, double minHeight = 0) => new TextBox
    {
        PlaceholderText = placeholder,
        InnerLeftContent = Icon("search", IconSize.S).With(i =>
        {
            i.Opacity = .7;   // the box's text color, quieter: it follows the box under the pointer too
            i.Margin = new Thickness(Space.L, 0, 0, 0);
            i.VerticalAlignment = VerticalAlignment.Center;
        })
    }.With(t => { if (minHeight > 0) { t.MinHeight = minHeight; } });
}
