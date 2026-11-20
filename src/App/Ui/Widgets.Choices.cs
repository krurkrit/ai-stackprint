// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Threading;

namespace AiStackprint.Ui;

internal static partial class W
{
    /// <summary>
    /// Calls <paramref name="changed"/> with the text once typing pauses (150 ms), not on every key, and only when the text differs from the
    /// last one reported (a text set in code, or typed and undone, is no change). Nothing is reported once the box has left the window:
    /// a late call would rebuild a page that is gone.
    /// </summary>
    public static TextBox OnPause(this TextBox box, Action<string> changed)
    {
        var last = box.Text ?? "";
        var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            if ((box.Text ?? "") != last)
            {
                last = box.Text ?? "";
                changed(last);
            }
        };
        box.TextChanged += (_, _) =>
        {
            wait.Stop();
            wait.Start();
        };
        box.DetachedFromVisualTree += (_, _) => wait.Stop();
        return box;
    }

    /// <summary>
    /// One check box per value; <paramref name="off"/> holds the unchecked values (empty = all checked).
    /// The last checked box cannot be unchecked: none would hide everything.
    /// </summary>
    public static Control Checks(IReadOnlyList<(Control Label, string Value)> opts, HashSet<string> off, Action changed)
    {
        var wrap = new WrapPanel
        {
            ItemSpacing = Space.XL,
            LineSpacing = Space.S
        };
        foreach (var (label, value) in opts)
        {
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = !off.Contains(value)
            };
            cb.IsCheckedChanged += (_, _) =>
            {
                if (cb.IsChecked == true)
                {
                    if (!off.Remove(value))
                    {
                        return;
                    }
                }
                else if (opts.Count(o => !off.Contains(o.Value)) <= 1)
                {
                    cb.IsChecked = true;
                    return;
                }
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
        var wrap = new WrapPanel
        {
            ItemSpacing = Space.XL,
            LineSpacing = Space.S
        };
        var group = "r" + ++_radioGroups;
        var chosen = current;   // follows the clicks: comparing with the first value missed a return to it ("All" after "Agent only")
        foreach (var (label, value) in opts)
        {
            var rb = new RadioButton
            {
                Content = label,
                GroupName = group,
                IsChecked = value == current
            };
            rb.IsCheckedChanged += (_, _) =>
            {
                if (rb.IsChecked == true && value != chosen)
                {
                    chosen = value;
                    pick(value);
                }
            };
            wrap.Children.Add(rb);
        }
        return wrap;
    }

    /// <summary>A single check box with a change callback.</summary>
    public static CheckBox Check(string label, bool on, Action<bool> set)
    {
        var cb = new CheckBox
        {
            Content = label,
            IsChecked = on
        };
        cb.IsCheckedChanged += (_, _) => set(cb.IsChecked == true);
        return cb;
    }
}
