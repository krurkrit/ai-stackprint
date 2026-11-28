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
    /// <summary>WCAG 2.2 AAA (1.4.6): status text reads at 7:1 (the app's minimum 7.1) on the card and the main area; dark danger text is the exception.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Status_text_reads_at_7_to_1_on_both_backgrounds(bool dark)
    {
        var (error, warning, success, background, main) = Theme.Status(dark);
        // Dark danger text is the pure red of its mark by choice (Theme.States): it only has to show as a mark does, at 3:1
        foreach (var (c, least) in new[] { (error, dark ? 3 : Contrast.Minimum), (warning, Contrast.Minimum), (success, Contrast.Minimum) })
        {
            foreach (var bg in new[] { background, main })
            {
                var ratio = Contrast.Ratio(c.ToUInt32(), bg.ToUInt32());
                Assert.True(ratio >= least, $"{c} on {bg}: {ratio:0.00}:1");
            }
        }
    }

    /// <summary>WCAG 1.4.11: status icons, bars and dots need 3:1 on the card.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Status_marks_show_on_the_card(bool dark)
    {
        var card = Theme.Status(dark).Background.ToUInt32();
        var (error, warning, success) = Theme.Marks(dark);
        foreach (var c in new[] { error, warning, success })
        {
            Assert.True(Contrast.Ratio(c.ToUInt32(), card) >= 3, $"{c} on the card");
        }
    }

    /// <summary>Text, muted text and links read at 7.1:1 on every surface and tint text sits on: hover, selected, text selection, a new log line.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Text_muted_text_and_links_read_at_7_to_1_on_every_surface_and_tint(bool dark)
    {
        var (text, muted, link) = Theme.Inks(dark);
        foreach (var bg in Theme.TextBackgrounds(dark))
        {
            foreach (var (name, ink) in new[] { ("text", text), ("muted", muted) })
            {
                var ratio = Contrast.Ratio(ink.ToUInt32(), bg.ToUInt32());
                Assert.True(ratio >= Contrast.Minimum, $"{name} {ink} on {bg}: {ratio:0.00}:1");
            }
        }

        var (_, _, _, card, main) = Theme.Status(dark);
        foreach (var bg in new[] { card, main })
        {
            Assert.True(Contrast.Ratio(link.ToUInt32(), bg.ToUInt32()) >= Contrast.Minimum, $"link {link} on {bg}");
        }
    }

    /// <summary>The chart palette: 8 colors per theme, each hex the sRGB color of its OKLCH (as rounded), all different, all marks at 3:1 on the card.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_chart_colors_match_their_oklch_and_show_on_the_card(bool dark)
    {
        var card = Theme.Status(dark).Background.ToUInt32();
        var palette = dark ? Theme.DarkChart : Theme.LightChart;
        Assert.Equal(W.ChartSlots, palette.Length);
        Assert.Equal(palette.Length, palette.Select(p => p.Hex).Distinct().Count());
        foreach (var p in palette)
        {
            var (l, c, h) = Oklch.From(Theme.ParseHex(p.Hex)!.Value.ToUInt32());
            Assert.Equal(p.Lightness, l, .005);
            Assert.Equal(p.Chroma, c, .0015);
            Assert.True(Math.Abs(p.Hue - h) <= 1, $"{p.Hex}: hue {h:F1} against {p.Hue}");
            Assert.True(Contrast.Ratio(Theme.ParseHex(p.Hex)!.Value.ToUInt32(), card) >= 3, $"{p.Hex} on the card");
        }
    }

    /// <summary>A color follows the thing, not its rank: token series, categories and levels each keep their slot; past the last slot is "Other" in gray.</summary>
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Every_series_keeps_its_own_chart_slot(string theme)
    {
        var settings = SettingsStore.Defaults();
        settings.Theme = theme;
        Theme.Appearance(Application.Current!, settings);
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        object? Get(string key) => Application.Current!.TryGetResource(key, variant, out var v) ? v : null;
        Assert.Equal(Get(W.ChartKey(1)), Get(W.ChartIn));
        Assert.Equal(Get(W.ChartKey(4)), Get(W.ChartRead));
        Assert.All(Theme.Categories.Select((c, i) => (c, i)), t => Assert.Equal(Get(W.ChartKey(t.i + 1)), Get(W.CategoryKey(t.c))));
        Assert.All(Theme.LevelNames.Select((l, i) => (l, i)), t => Assert.Equal(Get(W.ChartKey(t.i + 1)), Get(W.LevelKey(t.l))));
        Assert.Equal(W.Muted, W.ChartKey(W.ChartSlots + 1));
        Assert.Equal(W.Muted, W.CategoryKey("something new"));
        Assert.Equal((W.Muted, W.ChartKey(3)), (W.AgentKey(0), W.AgentKey(3)));
        Theme.Appearance(Application.Current!, SettingsStore.Defaults());
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
        // a drop-down under the pointer looks like a text box under the pointer: the same background, the accent border
        Assert.Equal(Get("ComboBoxBackground"), Get("ComboBoxBackgroundPointerOver"));
        Assert.Equal(Get(W.Accent), Get("ComboBoxBorderBrushPointerOver"));
        Assert.NotEqual(Get(W.Accent), Get("ComboBoxDropDownBorderBrush"));   // an open list keeps Fluent's neutral border
        Assert.Equal(new CornerRadius(Radius.M), Get("ControlCornerRadius"));
        Assert.Equal(new CornerRadius(Radius.L), Get("OverlayCornerRadius"));
    }

    /// <summary>The pop-up of an ⓘ button has the selected theme button's color: its text (full strength: the quieter muted gray is below 7:1 on it) reads at 7:1 on it, a custom accent too.</summary>
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("#6FA8FF")]
    [InlineData("#FFD43B")]
    public void The_info_pop_up_text_reads_at_7_to_1_on_its_background(string? accent)
    {
        var app = Application.Current!;
        var s = SettingsStore.Defaults();
        s.Light.Accent = accent;
        s.Dark.Accent = accent;
        Theme.Appearance(app, s);
        try
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                uint Get(string key) => app.Resources.TryGetResource(key, variant, out var v) && v is Avalonia.Media.ISolidColorBrush b ? b.Color.ToUInt32() : throw new KeyNotFoundException(key);
                var bg = Get(W.SelectedSolid);
                Assert.Equal(0xFFu, bg >> 24);   // opaque
                var text = Get(W.Text);
                Assert.True(Contrast.Ratio(text, bg) >= Contrast.Minimum, $"{variant} {accent}: {Contrast.Ratio(text, bg):0.00}:1");
            }
        }
        finally
        {
            Theme.Appearance(app, SettingsStore.Defaults());
        }
    }

    /// <summary>WCAG 2.2 AAA (1.4.6): button text needs 7:1 in every state, hover and pressed too; the app's minimum is 7.1. Each state is a different color.</summary>
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("#6FA8FF")]   // a light blue: white text on it is below 7:1, so the fill is moved
    [InlineData("#FFD43B")]   // a pale yellow: dark text
    [InlineData("#808080")]   // mid gray: the hardest case for either text color
    public void The_accent_button_text_reads_at_7_to_1_at_rest_under_the_pointer_and_pressed(string? accent)
    {
        var app = Application.Current!;
        var s = SettingsStore.Defaults();
        s.Light.Accent = accent;
        s.Dark.Accent = accent;
        Theme.Appearance(app, s);
        try
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                uint Get(string key) => app.Resources.TryGetResource(key, variant, out var v) && v is Avalonia.Media.ISolidColorBrush b ? b.Color.ToUInt32() : throw new KeyNotFoundException(key);
                var fills = new[] { "", "PointerOver", "Pressed" }.Select(st => (State: st, Fill: Get("AccentButtonBackground" + st), Text: Get("AccentButtonForeground" + st))).ToList();
                foreach (var (state, fill, text) in fills)
                {
                    var ratio = Contrast.Ratio(text, fill);
                    Assert.True(ratio >= Contrast.Minimum, $"{variant} {accent} {state}: {ratio:0.00}:1");
                }

                Assert.Equal(3, fills.Select(f => f.Fill).Distinct().Count());   // hover and pressed show a change
                Assert.True(Contrast.Ratio(0xFF000000, fills[1].Fill) > Contrast.Ratio(0xFF000000, fills[0].Fill), $"{variant} {accent}: hover is lighter");
            }
        }
        finally
        {
            Theme.Appearance(app, SettingsStore.Defaults());
        }
    }
}
