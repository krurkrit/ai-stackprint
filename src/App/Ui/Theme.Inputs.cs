// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;

namespace AiStackprint.Ui;

/// <summary>
/// One height and one vertical rhythm for every input: text box, number box, drop-down, check box and radio button.
/// The gap above the first line equals the gap below the last line, and the first line of a multi-line input sits
/// where a one-line input shows its text. Both come from the font's line height, so they hold at any font and size.
/// </summary>
internal static partial class Theme
{
    private const double InputBorder = 1;   // Fluent input border width
    private const double MinInputGap = 5;   // smallest gap between the border and the text

    private static Styles? _inputStyles;   // the app's current input styles, replaced when the font changes

    /// <summary>Height of a one-line input at the current font: the line height plus equal whole-pixel gaps, about 32 px (Fluent UI 2 default control; 33 for Inter at 14 px).</summary>
    public static double InputHeight { get; private set; } = 33;

    /// <summary>Gap between the top edge of an input and its first line of text (and between its last line and the bottom edge).</summary>
    public static double InputGap { get; private set; } = 8;

    /// <summary>Line height of the UI font at <paramref name="size"/>, as text boxes and labels lay it out.</summary>
    public static double LineHeight(double size) =>
        new TextLayout("Ag", new Typeface(Font), size, null).Height;

    private static void Inputs(Application app, double fontSize)
    {
        // Whole pixels, as layout rounding places them: a fractional gap would round to 1 px more on one side.
        // Fluent controls take their corners from these two resources: the same radii as our own controls (Fluent UI 2: medium 4, overlays 8)
        app.Resources["ControlCornerRadius"] = new CornerRadius(Radius.M);
        app.Resources["OverlayCornerRadius"] = new CornerRadius(Radius.L);
        var line = Math.Ceiling(LineHeight(fontSize));
        InputGap = Math.Max(MinInputGap + InputBorder, Math.Ceiling((32 - line) / 2));
        InputHeight = line + 2 * InputGap;
        var pad = InputGap - InputBorder;   // text and number boxes, drop-downs: the gap sits inside their border
        app.Resources["CheckBoxMinHeight"] = InputHeight;   // Fluent centers the box in this height
        app.Resources["RadioButtonMinHeight"] = InputHeight;
        var styles = new Styles();
        styles.Add(Style<TextBox>(new Thickness(Space.L, pad, Space.S, pad), VerticalAlignment.Center));
        styles.Add(new Style(x => x.OfType<TextBox>().PropertyEquals(TextBox.AcceptsReturnProperty, true))
        {
            Setters = { new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Top) }   // text areas: first line at the top gap
        });
        styles.Add(Style<NumericUpDown>(new Thickness(Space.L, pad, Space.S, pad), VerticalAlignment.Center));
        styles.Add(Style<ComboBox>(new Thickness(Space.L, pad, 0, pad), VerticalAlignment.Center));
        // Check boxes and radio buttons have no border around the text: the label starts at the gap, so a wrapped label's
        // first line stays level with the box and the last line keeps the same gap below.
        styles.Add(Style<CheckBox>(new Thickness(Space.M, InputGap, 0, InputGap), VerticalAlignment.Top));
        styles.Add(Style<RadioButton>(new Thickness(Space.M, InputGap, 0, InputGap), VerticalAlignment.Top));
        if (_inputStyles != null)
        {
            app.Styles.Remove(_inputStyles);
        }

        _inputStyles = styles;
        app.Styles.Add(styles);
    }

    private static Style Style<T>(Thickness padding, VerticalAlignment content) where T : TemplatedControl => new(x => x.OfType<T>())
    {
        Setters =
        {
            new Setter(Layoutable.MinHeightProperty, InputHeight),
            new Setter(TemplatedControl.PaddingProperty, padding),
            new Setter(ContentControl.VerticalContentAlignmentProperty, content),
        }
    };
}
