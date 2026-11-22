// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Theme colors meet WCAG contrast (needs the app: Theme uses Avalonia colors, so the tests run on the headless UI thread like the others; a plain test on another thread made Avalonia's render loop belong to that thread, and the next UI test failed to start).</summary>
public class ThemeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Status_colors_read_on_both_backgrounds(bool dark)
    {
        var (error, warning, success, background, main) = Theme.Status(dark);
        foreach (var c in new[] { error, warning, success })
        {
            foreach (var bg in new[] { background, main })
            {
                Assert.True(Contrast.Ratio(c.ToUInt32(), bg.ToUInt32()) >= 4.5, $"{c} on {bg}");
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Chart_colors_show_on_the_card(bool dark)
    {
        var card = Theme.Status(dark).Background.ToUInt32();
        var marks = Theme.Series(dark).Concat(Enumerable.Range(0, 4).Select(i => Theme.LevelDefault(dark, i)));
        foreach (var c in marks)
        {
            Assert.True(Contrast.Ratio(c.ToUInt32(), card) >= 3, $"{c} on the card");   // WCAG 1.4.11: chart marks need 3:1
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_date_picker_and_focused_inputs_take_the_same_border_colors_as_the_other_inputs_and_corners_follow_the_tokens(bool dark)
    {
        var app = Application.Current!;
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        object? Get(string key) => app.Resources.TryGetResource(key, variant, out var v) ? v : null;
        Assert.NotNull(Get("TextControlBorderBrush"));
        Assert.Equal(Get("TextControlBorderBrush"), Get("CalendarDatePickerBorderBrush"));
        Assert.Equal(Get("TextControlBorderBrushPointerOver"), Get("CalendarDatePickerBorderBrushPointerOver"));
        Assert.Equal(Get(W.Accent), Get("TextControlBorderBrushFocused"));   // the accent: Fluent UI 2 brand focus
        Assert.Equal(Get(W.Accent), Get("TextControlBorderBrushPointerOver"));   // text boxes: the accent border under the pointer
        Assert.Equal(Get("TextControlBackground"), Get("TextControlBackgroundPointerOver"));
        // a drop-down under the pointer looks like a side menu item under the pointer: the accent button's fill and text color
        Assert.Equal(Get("AccentButtonBackgroundPointerOver"), Get("ComboBoxBackgroundPointerOver"));
        Assert.Equal(Get("AccentButtonForegroundPointerOver"), Get("ComboBoxForegroundPointerOver"));
        Assert.Equal(new CornerRadius(Radius.M), Get("ControlCornerRadius"));
        Assert.Equal(new CornerRadius(Radius.L), Get("OverlayCornerRadius"));
    }
}
