// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;

namespace AiStackprint.Ui;

internal sealed partial class FontSetting
{
    /// <summary>Own family: a drop-down of the fonts. Linked: the parent's family, shown and not editable.</summary>
    private Control FamilyBox()
    {
        var linked = _set.Mode(_key, FontProp.Family) == FontMode.Link;
        var now = _set.Resolve(_key).Family;
        var box = AppearanceSection.FontCombo(_families, _role.DefaultFamily, now, f => Value(() => _set.SetName(_key, f)), width: Widths.FontFamily);
        box.IsEnabled = !linked;
        return box;
    }

    /// <summary>Own size: a number between the limits, snapped to the step. Adjusted: the offset (+2, -1). Linked: the parent's size, not editable.</summary>
    private Control SizeBox()
    {
        var mode = _set.Mode(_key, FontProp.Size);
        var l = _set.Limits(_key, FontProp.Size);
        var step = l.Step is > 0 ? l.Step.Value : 1;
        var span = Math.Max(l.Max!.Value - l.Min!.Value, step);
        var adjust = mode == FontMode.Adjust;
        var shown = adjust ? _set.Number(_key, FontProp.Size) ?? 0 : _set.Resolve(_key).Size;
        var box = Number(adjust ? -span : l.Min.Value, adjust ? span : l.Max.Value, step, shown, adjust ? "+0.##;-0.##;0" : "0.##", Widths.FontSize);
        box.IsEnabled = mode != FontMode.Link;
        Tip(box, FontProp.Size);
        Commit(box, v =>
        {
            Value(() => _set.SetNumber(_key, FontProp.Size, v));
            Tip(box, FontProp.Size);
            return adjust ? _set.Number(_key, FontProp.Size) ?? 0 : _set.Resolve(_key).Size;
        });
        return box;
    }

    /// <summary>Own weight: a drop-down of the allowed weights. Adjusted: the offset as a number. Linked: the parent's weight, not editable.</summary>
    private Control WeightBox()
    {
        var mode = _set.Mode(_key, FontProp.Weight);
        if (mode == FontMode.Adjust)
        {
            var l = _set.Limits(_key, FontProp.Weight);
            var all = _set.Weights(_key);
            var span = Math.Max(all[^1] - all[0], 100);
            var step = l.Step is > 0 ? l.Step.Value : 100;
            var box = Number(-span, span, step, _set.Number(_key, FontProp.Weight) ?? 0, "+0;-0;0", Widths.FontWeight);
            Tip(box, FontProp.Weight);
            Commit(box, v =>
            {
                Value(() => _set.SetNumber(_key, FontProp.Weight, v));
                Tip(box, FontProp.Weight);
                return _set.Number(_key, FontProp.Weight) ?? 0;
            });
            return box;
        }

        var weights = _set.Weights(_key);
        var items = weights.Select(w => FontWeights.Label(w)).ToList();
        var current = FontWeights.Label(_set.Resolve(_key).Weight);
        var combo = new ComboBox
        {
            ItemsSource = items,
            SelectedItem = items.Contains(current) ? current : null,
            Width = Widths.FontWeight,
            IsEnabled = mode != FontMode.Link
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex is >= 0 and var i && items[i] != current)
            {
                Value(() => _set.SetNumber(_key, FontProp.Weight, weights[i]));
            }
        };
        return combo;
    }

    private static NumericUpDown Number(double min, double max, double step, double value, string format, double width) => new()
    {
        Minimum = (decimal)min,
        Maximum = (decimal)max,
        Increment = (decimal)step,
        FormatString = format,
        Value = (decimal)Math.Clamp(value, min, max),
        Width = width,
        ShowButtonSpinner = false,   // the box is too narrow for the arrows; the keyboard arrows and typing work
        MinWidth = 0
    };

    /// <summary>Takes the typed number when the value changes and puts back what the rules made of it (clamped and snapped).</summary>
    private static void Commit(NumericUpDown box, Func<double, double> apply)
    {
        var busy = false;
        box.ValueChanged += (_, e) =>
        {
            if (busy || e.NewValue is not { } v)
            {
                return;
            }

            var fitted = apply((double)v);
            busy = true;
            box.Value = (decimal)Math.Clamp(fitted, (double)box.Minimum, (double)box.Maximum);
            busy = false;
        };
    }

    /// <summary>The tooltip of an adjusted field: the parent's value, the offset, the sum and the allowed value it came to.</summary>
    private void Tip(Control c, FontProp prop) => ToolTip.SetTip(c, _set.Describe(_key, prop) is { Length: > 0 } t ? t : null);
}
