// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace AiStackprint.Ui;

static partial class W
{
    public static Button Btn(string text, Action a, bool primary = false, string? icon = null)
    {
        object content = icon == null ? text : Row(Icon(icon, 16), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content, MinHeight = 36, Padding = new Thickness(14, 0), VerticalContentAlignment = VerticalAlignment.Center };
        if (primary)
        {
            b.Classes.Add("accent");
        }

        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Button for destructive actions (Snowy Owl error pair).</summary>
    public static Button DangerBtn(string text, Action a, string? icon = null) =>
        Btn(text, a, false, icon).Themed(Button.BackgroundProperty, ErrorFill).Themed(Button.ForegroundProperty, OnError);

    /// <summary>Small transparent icon-only button with a tooltip.</summary>
    public static Button IconBtn(string icon, string tip, Action a, string key = Muted)
    {
        var b = new Button { Content = Icon(icon, 16).Themed(TextElement.ForegroundProperty, key), Padding = new Thickness(8), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Pill-shaped filter buttons; the selected one uses the accent style.</summary>
    public static Control Pills(IEnumerable<(string label, string value)> opts, string current, Action<string> pick)
    {
        // Spacing on the panel, not a margin on each button: one line is exactly 32 px, so a label beside it lines up
        var wrap = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (label, value) in opts)
        {
            var b = new Button { Content = label, MinHeight = 32, CornerRadius = new CornerRadius(16) };
            if (value == current)
            {
                b.Classes.Add("accent");
            }

            b.Click += (_, _) => pick(value);
            wrap.Children.Add(b);
        }
        return wrap;
    }

    public static Control Row(params Control[] c)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
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
        var name = Txt(label, 13, false, Muted).With(t => { t.Width = 80; t.VerticalAlignment = VerticalAlignment.Center; });
        DockPanel.SetDock(name, Dock.Left);
        var opts = new WrapPanel { ItemSpacing = 8, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var x in c) { x.VerticalAlignment = VerticalAlignment.Center; opts.Children.Add(x); }
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
    public static Control Counted(Control label, int n) => Row(label, Txt(n.ToString("N0"), 13, false, Muted));

    /// <summary>Search field: text and icon centered vertically. 40 px tall on list pages; pass 0 to match a row of 32-px controls.</summary>
    public static TextBox SearchBox(string placeholder, double minHeight = 40) => new()
    {
        PlaceholderText = placeholder,
        MinHeight = minHeight,
        VerticalContentAlignment = VerticalAlignment.Center,
        InnerLeftContent = Icon("search", 16).Themed(TextElement.ForegroundProperty, Muted).With(i => { i.Margin = new Thickness(10, 0, 0, 0); i.VerticalAlignment = VerticalAlignment.Center; })
    };

    /// <summary>
    /// One check box per value; <paramref name="off"/> holds the unchecked values (empty = all checked).
    /// The last checked box cannot be unchecked: none would hide everything.
    /// </summary>
    public static Control Checks(IReadOnlyList<(Control Label, string Value)> opts, HashSet<string> off, Action changed)
    {
        var wrap = new WrapPanel { ItemSpacing = 16, LineSpacing = 6 };
        foreach (var (label, value) in opts)
        {
            var cb = new CheckBox { Content = label, IsChecked = !off.Contains(value), MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
            cb.IsCheckedChanged += (_, _) =>
            {
                if (cb.IsChecked == true)
                {
                    if (!off.Remove(value))
                    {
                        return;
                    }
                }
                else if (opts.Count(o => !off.Contains(o.Value)) <= 1) { cb.IsChecked = true; return; }
                else
                {
                    off.Add(value);
                }

                changed();
            };
            wrap.Children.Add(cb);
        }
        return wrap;
    }

    static int _radioGroups;

    /// <summary>One choice among <paramref name="opts"/> as radio buttons.</summary>
    public static Control Radios(IEnumerable<(string label, string value)> opts, string current, Action<string> pick)
    {
        var wrap = new WrapPanel { ItemSpacing = 16, LineSpacing = 6 };
        string group = "r" + ++_radioGroups;
        foreach (var (label, value) in opts)
        {
            var rb = new RadioButton { Content = label, GroupName = group, IsChecked = value == current, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true && value != current) { pick(value); } };
            wrap.Children.Add(rb);
        }
        return wrap;
    }

    /// <summary>A single check box with a change callback.</summary>
    public static CheckBox Check(string label, bool on, Action<bool> set)
    {
        var cb = new CheckBox { Content = label, IsChecked = on, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        cb.IsCheckedChanged += (_, _) => set(cb.IsChecked == true);
        return cb;
    }
}
