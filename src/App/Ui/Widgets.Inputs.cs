// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AiStackprint.Ui;

internal static partial class W
{
    public static Button Btn(string text, Action a, bool primary = false, string? icon = null)
    {
        object content = icon == null ? text : Row(Icon(icon, IconSize.S), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content, MinHeight = Theme.InputHeight, Padding = new Thickness(Space.XL, 0), VerticalContentAlignment = VerticalAlignment.Center };
        if (primary)
        {
            b.Classes.Add("accent");
        }

        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Button for destructive actions (Snowy Owl error pair).</summary>
    public static Button DangerBtn(string text, Action a, string? icon = null) =>
        Btn(text, a, false, icon).Themed(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, ErrorFill).Themed(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, OnError);

    /// <summary>Small transparent icon-only button with a tooltip.</summary>
    public static Button IconBtn(string icon, string tip, Action a, string key = Muted)
    {
        var b = new Button { Content = Icon(icon, IconSize.S).Themed(TextElement.ForegroundProperty, key), Padding = new Thickness(Space.M), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Pill-shaped filter buttons; the selected one uses the accent style.</summary>
    public static Control Pills(IEnumerable<(string label, string value)> opts, string current, Action<string> pick)
    {
        // Spacing on the panel, not a margin on each button: one line is exactly 32 px, so a label beside it lines up
        var wrap = new WrapPanel { ItemSpacing = Space.S, LineSpacing = Space.S };
        foreach (var (label, value) in opts)
        {
            var b = new Button { Content = label, MinHeight = Sizes.CompactControl, CornerRadius = new CornerRadius(Radius.Pill) };
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
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.M };
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
        var name = Txt(label, TextSize.Small, false, Muted).With(t => { t.Width = Widths.LabelXS; t.VerticalAlignment = VerticalAlignment.Center; });
        DockPanel.SetDock(name, Dock.Left);
        var opts = new WrapPanel { ItemSpacing = Space.M, LineSpacing = Space.XS, VerticalAlignment = VerticalAlignment.Center };
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
    public static Control Counted(Control label, int n) => Row(label, Txt(n.ToString("N0"), TextSize.Small, false, Muted));

    /// <summary>Search field with an icon; the same height as other inputs (<see cref="Theme.InputHeight"/>) unless <paramref name="minHeight"/> asks for more.</summary>
    public static TextBox SearchBox(string placeholder, double minHeight = 0) => new TextBox
    {
        PlaceholderText = placeholder,
        InnerLeftContent = Icon("search", IconSize.S).Themed(TextElement.ForegroundProperty, Muted).With(i => { i.Margin = new Thickness(Space.L, 0, 0, 0); i.VerticalAlignment = VerticalAlignment.Center; })
    }.With(t => { if (minHeight > 0) { t.MinHeight = minHeight; } });

    /// <summary>Calls <paramref name="changed"/> with the text once typing pauses (150 ms), not on every key.</summary>
    public static TextBox OnPause(this TextBox box, Action<string> changed)
    {
        var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        wait.Tick += (_, _) => { wait.Stop(); changed(box.Text ?? ""); };
        box.TextChanged += (_, _) => { wait.Stop(); wait.Start(); };
        return box;
    }

    /// <summary>
    /// One check box per value; <paramref name="off"/> holds the unchecked values (empty = all checked).
    /// The last checked box cannot be unchecked: none would hide everything.
    /// </summary>
    public static Control Checks(IReadOnlyList<(Control Label, string Value)> opts, HashSet<string> off, Action changed)
    {
        var wrap = new WrapPanel { ItemSpacing = Space.XL, LineSpacing = Space.S };
        foreach (var (label, value) in opts)
        {
            var cb = new CheckBox { Content = label, IsChecked = !off.Contains(value) };
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

    private static int _radioGroups;

    /// <summary>One choice among <paramref name="opts"/> as radio buttons.</summary>
    public static Control Radios(IEnumerable<(string label, string value)> opts, string current, Action<string> pick)
    {
        var wrap = new WrapPanel { ItemSpacing = Space.XL, LineSpacing = Space.S };
        var group = "r" + ++_radioGroups;
        foreach (var (label, value) in opts)
        {
            var rb = new RadioButton { Content = label, GroupName = group, IsChecked = value == current };
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true && value != current) { pick(value); } };
            wrap.Children.Add(rb);
        }
        return wrap;
    }

    /// <summary>A single check box with a change callback.</summary>
    public static CheckBox Check(string label, bool on, Action<bool> set)
    {
        var cb = new CheckBox { Content = label, IsChecked = on };
        cb.IsCheckedChanged += (_, _) => set(cb.IsChecked == true);
        return cb;
    }
}
