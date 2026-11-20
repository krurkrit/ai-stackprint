// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Inputs share one height; text sits in the middle, and multi-line inputs keep the same gap above the first line and below the last.</summary>
public class InputLayoutTests
{
    private const string FiveLines = "one\ntwo\nthree\nfour\nfive";

    [AvaloniaFact]
    public void Inputs_share_one_height_and_balanced_text_at_every_font_size()
    {
        try
        {
            foreach (var size in new[] { 12d, 14d, 18d })
            {
                var settings = SettingsStore.Defaults();
                new AiStackprint.Services.FontRuleSet(settings.Fonts).SetNumber("body", AiStackprint.Services.FontProp.Size, size);
                Theme.Appearance(Application.Current!, settings);
                Check(size);
            }
        }
        finally
        {
            Theme.Appearance(Application.Current!, SettingsStore.Defaults());
        }
    }

    private static void Check(double size)
    {
        var one = new Control[]
        {
            new TextBox { Text = "Hello" },
            W.SearchBox("Search"),
            new NumericUpDown { Value = 3 },
            new ComboBox { ItemsSource = new[] { "One" }, SelectedIndex = 0 },
            new CheckBox { Content = "Check" },
            new RadioButton { Content = "Radio" },
        };
        var area = new TextBox { Text = FiveLines, AcceptsReturn = true };
        var tallArea = new TextBox { Text = FiveLines, AcceptsReturn = true, Height = 400 };
        var wrapped = new CheckBox { Content = new TextBlock { Text = "A long label that wraps onto several lines", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, Width = 90 };
        var panel = new StackPanel { Width = 300 };
        panel.Children.AddRange(one);
        panel.Children.AddRange([area, tallArea, wrapped]);
        Ui.Show(panel);

        var h = Theme.InputHeight;
        var gap = Theme.InputGap;
        foreach (var c in one)
        {
            var (top, bottom) = Gaps(c);
            // FluentAvalonia's drop-down text line is 2 px taller than ours at 12 px: same height, gaps 1 px less on each side
            var slack = c is ComboBox ? 1 : 0;
            Assert.Equal((size, c.GetType().Name, h), (size, c.GetType().Name, c.Bounds.Height));
            Assert.True(Math.Abs(top - gap) <= slack && Math.Abs(bottom - gap) <= slack, $"{size}px {c.GetType().Name}: gaps {top}/{bottom}, expected {gap}");
        }

        foreach (var c in new Control[] { area, wrapped })
        {
            var (top, bottom) = Gaps(c);
            Assert.True(c.Bounds.Height > h, $"{size}px {c.GetType().Name}: expected several lines");
            Assert.Equal((size, c.GetType().Name, gap, gap), (size, c.GetType().Name, top, bottom));
        }

        Assert.Equal(gap, Gaps(tallArea).Top);   // a fixed-height text area starts at the same line, not in the middle

        // The check box stays level with the first line of a wrapped label.
        var box = Glyph(wrapped);
        var label = Text(wrapped);
        var lineHeight = label.Bounds.Height / Lines(label);
        Assert.Equal(Top(box, wrapped) + box.Bounds.Height / 2, Top(label, wrapped) + lineHeight / 2, 1.0);   // FluentAvalonia's box is a little larger: within 1 px of level
    }

    [AvaloniaFact]
    public void A_date_picker_has_the_height_and_text_position_of_a_text_box_and_a_focused_input_has_one_line_on_every_side()
    {
        var picker = new CalendarDatePicker { SelectedDate = new DateTime(2026, 6, 9, 0, 0, 0, DateTimeKind.Unspecified), Width = 300 };
        var box = new TextBox { Text = "2026-06-09" };
        Ui.Show(new StackPanel { Children = { picker, box } });
        Assert.Equal(box.Bounds.Height, picker.Bounds.Height);
        var inner = picker.GetVisualDescendants().OfType<TextPresenter>().First();
        var text = box.GetVisualDescendants().OfType<TextPresenter>().First();
        Assert.Equal(text.TranslatePoint(default, box), inner.TranslatePoint(default, picker));

        Assert.True(Application.Current!.TryGetResource("TextControlBorderThemeThicknessFocused", Avalonia.Styling.ThemeVariant.Light, out var focused));
        Assert.Equal(new Thickness(1), focused);   // the theme's own is thicker at the bottom
    }

    [AvaloniaFact]
    public void Pill_labels_sit_in_the_middle_of_the_pill()
    {
        var pills = W.Pills([("Warning", "w"), ("All", "")], "", _ => { });
        Ui.Show(new StackPanel { Children = { pills } });
        foreach (var b in pills.GetVisualDescendants().OfType<Button>())
        {
            var (top, bottom) = Gaps(b);
            Assert.True(Math.Abs(top - bottom) <= 1, $"{b.Content}: {top} above the text, {bottom} below (1 px is layout rounding of an odd gap)");
        }
    }

    private static (double Top, double Bottom) Gaps(Control c)
    {
        var t = Text(c);
        var top = Top(t, c);
        return (top, c.Bounds.Height - top - t.Bounds.Height);
    }

    private static Visual Text(Control c) => c switch
    {
        TextBox or NumericUpDown => c.GetVisualDescendants().OfType<TextPresenter>().First(),
        ComboBox => c.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "One"),
        _ => c.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text)),
    };

    private static Visual Glyph(Control c) =>
        c.GetVisualDescendants().OfType<Control>().First(v => v.Name is "NormalRectangle" or "OuterEllipse");

    private static int Lines(Visual text) => text is TextBlock t ? t.TextLayout.TextLines.Count : 1;

    private static double Top(Visual v, Visual root) => v.TranslatePoint(default, root)!.Value.Y;
}
