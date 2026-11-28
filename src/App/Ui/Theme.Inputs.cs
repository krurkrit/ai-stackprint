// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace AiStackprint.Ui;

/// <summary>
/// One height and one vertical rhythm for every input: text box, number box, drop-down, check box and radio button.
/// The gap above the first line equals the gap below the last line, and the first line of a multi-line input sits
/// where a one-line input shows its text. Both come from the font's line height, so they hold at any font and size.
/// </summary>
internal static partial class Theme
{
    private const double InputBorder = 1;   // Fluent input border width
    private static Thickness _datePad;
    private static bool _datePickerHooked;
    private const double MinInputGap = 5;   // smallest gap between the border and the text

    private static Styles? _inputStyles;   // the app's current input styles, replaced when the font changes

    /// <summary>Height of a one-line input at the current font: the line height plus equal whole-pixel gaps, about 32 px (Fluent UI 2 default control; 33 for Inter at 14 px).</summary>
    public static double InputHeight { get; private set; } = 33;

    /// <summary>Gap between the top edge of an input and its first line of text (and between its last line and the bottom edge).</summary>
    public static double InputGap { get; private set; } = 8;

    /// <summary>The top and bottom padding inside a text or number box: the gap less the border.</summary>
    public static double InputPad => InputGap - InputBorder;

    /// <summary>Line height of the UI font at <paramref name="size"/>, as text boxes and labels lay it out.</summary>
    public static double LineHeight(double size) =>
        new TextLayout("Ag", new Typeface(Font), size, null).Height;

    private static void Inputs(Application app, double fontSize)
    {
        // Whole pixels, as layout rounding places them: a fractional gap would round to 1 px more on one side.
        // Fluent controls take their corners from these two resources: the same radii as our own controls (Fluent UI 2: medium 4, overlays 8)
        app.Resources["ControlCornerRadius"] = new CornerRadius(Radius.M);
        app.Resources["OverlayCornerRadius"] = new CornerRadius(Radius.L);
        app.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(InputBorder);   // focused: the same line on every side (Fluent UI 2 thickens the bottom edge)
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
        styles.Add(new Style(x => x.OfType<RadioButton>().Class(":checked").Template().OfType<Avalonia.Controls.Shapes.Ellipse>().Name("CheckGlyph"))
        {
            Setters = { new Setter(Layoutable.WidthProperty, Sizes.RadioDot), new Setter(Layoutable.HeightProperty, Sizes.RadioDot), new Setter(Visual.OpacityProperty, .9) }
        });
        // A filter menu's pop-up has a drop-down list's inner padding (open drop-downs and their lists keep Fluent's neutral borders)
        styles.Add(new Style(x => x.OfType<FlyoutPresenter>().Class(FilterMenu.PresenterClass))
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(Space.XS)) }
        });
        // An ⓘ pop-up: only its dialog card shows (with room for the card's shadow), not the presenter's own fill, border and padding
        styles.Add(new Style(x => x.OfType<FlyoutPresenter>().Class(InfoFlyout.PresenterClass))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MaxWidthProperty, double.PositiveInfinity),
                new Setter(Layoutable.MaxHeightProperty, double.PositiveInfinity)
            }
        });
        if (_inputStyles != null)
        {
            app.Styles.Remove(_inputStyles);
        }

        _datePad = new Thickness(Space.L, pad, Space.S, pad);
        if (!_datePickerHooked)
        {
            // The date picker's own text box keeps a padding of its template that a style cannot change: set it when the template is made, so the date sits where a text box's text does
            _datePickerHooked = true;
            TemplatedControl.TemplateAppliedEvent.AddClassHandler<CalendarDatePicker>((_, e) =>
            {
                if (e.NameScope.Find<TextBox>("PART_TextBox") is { } box)
                {
                    box.Padding = _datePad;
                }

                // A larger calendar glyph (the template's own is small beside the text)
                if (e.NameScope.Find<Button>("PART_Button") is { } button)
                {
                    button.Loaded += (_, _) =>
                    {
                        foreach (var glyph in button.GetVisualDescendants().OfType<FluentAvalonia.UI.Controls.FAFontIcon>())
                        {
                            glyph.FontSize = IconSize.M;
                        }
                    };
                }
            });
            TemplatedControl.TemplateAppliedEvent.AddClassHandler<CheckBox>((_, e) => GlyphCell(e, "NormalRectangle", "CheckBoxMinHeight"));
            TemplatedControl.TemplateAppliedEvent.AddClassHandler<RadioButton>((_, e) => GlyphCell(e, "OuterEllipse", "RadioButtonMinHeight"));
        }

        _inputStyles = styles;
        app.Styles.Add(styles);
    }

    /// <summary>
    /// The box (or circle) sits in the middle of a cell the template makes 32 px tall, a value a style cannot change. An input is taller at
    /// some fonts and sizes (Segoe UI at 18 px: 36), and then the box sat above a label's first line. The cell follows the input height.
    /// </summary>
    private static void GlyphCell(TemplateAppliedEventArgs e, string part, string heightKey)
    {
        if (e.NameScope.Find<Control>(part)?.GetVisualParent() is Grid cell)
        {
            cell.Bind(Layoutable.HeightProperty, cell.GetResourceObservable(heightKey));
        }
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
