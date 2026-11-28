// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AiStackprint.Ui;

internal static partial class W
{
    /// <summary>
    /// A number box laid out as [−  value  +]: the minus and plus buttons sit inside the box and step by its increment within its limits
    /// (turned off at a limit); the number can also be typed. Replaces the up and down arrows of <see cref="NumericUpDown"/>.
    /// The box is as wide as the buttons and <paramref name="widest"/>, the longest text it shows (such as "00" or "100%").
    /// </summary>
    public static NumericUpDown Stepper(NumericUpDown box, string widest)
    {
        box.ShowButtonSpinner = false;
        box.TextAlignment = TextAlignment.Center;
        box.HorizontalContentAlignment = HorizontalAlignment.Center;
        box.Padding = new Thickness(0, Theme.InputPad, 0, Theme.InputPad);   // the buttons give the space at the sides
        var minus = StepButton("subtract", "Decrease", box, -1);
        var plus = StepButton("add", "Increase", box, 1);
        box.InnerLeftContent = minus;
        box.InnerRightContent = plus;
        box.MinWidth = 0;
        box.Width = TextWidth(widest) + 2 * (StepButtonWidth + Space.XS + Stroke.Thin);   // a small gap between each button and the text
        void Limits()
        {
            minus.IsEnabled = box.Value is not { } v || v > box.Minimum;
            plus.IsEnabled = box.Value is not { } w || w < box.Maximum;
        }

        box.ValueChanged += (_, _) => Limits();
        Limits();
        return box;
    }

    private const double StepButtonWidth = Space.XXS + Space.S + IconSize.S + Space.S + Space.XXS;   // margin, padding, icon

    /// <summary>The width of <paramref name="text"/> in the font and size of an input box, in whole pixels.</summary>
    internal static double TextWidth(string text, FontFamily? font = null) =>
        Math.Ceiling(new Avalonia.Media.TextFormatting.TextLayout(text, new Typeface(font ?? Theme.Font), Size(TextSize.Body), null).WidthIncludingTrailingWhitespace);

    private static Button StepButton(string icon, string tip, NumericUpDown box, int sign)
    {
        var b = new Button
        {
            Content = Icon(icon, IconSize.S).Themed(TextElement.ForegroundProperty, Muted),
            Padding = new Thickness(Space.S),
            Margin = new Thickness(Space.XXS),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false   // the box keeps the focus; the keyboard arrows step it too
        };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) =>
        {
            var was = box.Value;
            box.Value = Math.Clamp((box.Value ?? 0) + sign * box.Increment, box.Minimum, box.Maximum);
            if (box.Value != was && box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is { } number)
            {
                Motion.Nudge(number, sign * Space.XS);   // up from below for +, down from above for −
            }
        };
        return b;
    }
}
