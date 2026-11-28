// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace AiStackprint.Ui;

internal static partial class Theme
{
    private static ResourceDictionary Keys(Palette p, ThemeColors o, ColorPaletteResources fluent)
    {
        Color fg = C(p.Fg), bg = C(p.Bg);
        var custom = ParseHex(o.Accent);
        var primary = custom ?? C(p.Primary);
        var onPrimary = custom == null ? C(p.OnPrimary) : W.BestText(primary, C(White), C(AccentTextDark));   // keep accent buttons readable
        var hover = ParseHex(o.Hover) ?? A(fg, p.HoverAlpha);
        var selection = ParseHex(o.Selection) ?? A(C(p.Selection), p.SelectionAlpha);
        fluent.Accent = primary;
        var d = new ResourceDictionary
        {
            [W.Background] = B(bg),
            [W.Text] = B(fg),
            [W.Surface] = B(C(p.Surface)),
            [W.SideKey] = B(C(p.Side)),
            [W.MainKey] = B(C(p.Main)),
            [W.TopKey] = B(C(p.Top)),
            [W.Muted] = B(C(p.Muted)),
            [W.BorderKey] = B(p.Border == null ? A(fg, p.DividerAlpha) : C(p.Border)),   // dark: a solid line
            [W.Subtle] = B(hover),
            [W.Selected] = B(A(primary, p.SelectedAlpha)),
            [W.SelectedSolid] = B(Color.FromUInt32(Services.Contrast.Blend(A(primary, p.SelectedAlpha).ToUInt32(), C(p.Top).ToUInt32()))),   // the selected theme button as seen on the top bar, opaque: for a pop-up, which has nothing under it to show through
            [W.Accent] = B(primary),
            [W.OnAccent] = B(onPrimary),
            [W.Link] = B(custom == null ? C(p.Link) : LinkFor(custom.Value, p)),
            [W.SelectionKey] = B(selection),
            ["TextControlSelectionHighlightColor"] = B(selection),
            // Status: text at 7.1:1 (ErrorKey, WarningKey, SuccessKey, SuccessText for "Installed"), marks for icons, bars and dots at 3:1
            [W.ErrorKey] = B(C(p.States.DangerText)),
            [W.WarningKey] = B(C(p.States.WarningText)),
            [W.SuccessKey] = B(C(p.States.SuccessText)),
            [W.SuccessText] = B(C(p.States.SuccessText)),
            [W.ErrorMark] = B(C(p.States.DangerMark)),
            [W.WarningMark] = B(C(p.States.WarningMark)),
            [W.SuccessMark] = B(C(p.States.SuccessMark)),
            [W.ErrorFill] = B(C(DangerFill)),
            [W.OnError] = B(Colors.White),
            [W.ErrorHover] = B(Shade(C(DangerFill), Colors.White, .15)),
            [W.ErrorPressed] = B(Shade(C(DangerFill), Colors.White, .28)),
            ["ButtonBackgroundPointerOver"] = B(hover),
            // New log lines/steps, fading out: amber fill behind the text and a solid ring (light: orange, dark: yellow)
            [W.HighlightKey] = B(A(C(Warning), p.HighlightAlpha)),
            [W.HighlightRingKey] = B(C(p == _dark ? Warning : OrangeAccent)),
            [W.ScrimKey] = B(A(Colors.Black, p == _dark ? .5 : .35)),   // behind the search overlay
        };
        // FluentAvalonia builds its accent fills from these seven colors (it would otherwise use its own purple)
        d["SystemAccentColor"] = primary;
        for (var i = 1; i <= 3; i++)
        {
            d["SystemAccentColorLight" + i] = Mix(primary, Colors.White, .2 * i);
            d["SystemAccentColorDark" + i] = Mix(primary, Colors.Black, .15 * i);
        }
        // its light theme fixes the fills when it loads (its purple), so the fills are set here too
        foreach (var (name, alpha) in new[] { ("Default", 1.0), ("Secondary", .9), ("Tertiary", .8) })
        {
            d["AccentFillColor" + name] = A(primary, alpha);
            d["AccentFillColor" + name + "Brush"] = B(A(primary, alpha));
        }

        // Charts: the 8 series colors, W.ChartKey(1) to (8). The token series, categories and levels each keep their own slot, so a color always means the same thing.
        var chart = p == _dark ? DarkChart : LightChart;
        for (var i = 0; i < chart.Length; i++)
        {
            d[W.ChartKey(i + 1)] = B(C(chart[i].Hex));
        }

        string[] series = [W.ChartIn, W.ChartOut, W.ChartWrite, W.ChartRead];
        for (var i = 0; i < series.Length; i++)
        {
            d[series[i]] = d[W.ChartKey(i + 1)];
        }

        for (var i = 0; i < Categories.Length; i++)
        {
            d[W.CategoryKey(Categories[i])] = d[W.ChartKey(i + 1)];
        }

        for (var i = 0; i < LevelNames.Length; i++)
        {
            d[W.LevelKey(LevelNames[i])] = ParseHex(o.Levels?.ElementAtOrDefault(i)) is { } level ? B(level) : d[W.ChartKey(i + 1)];
        }
        // Inputs sit on cards: card background and the divider color as border (cards themselves have no border)
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "ComboBoxBackground" })
        {
            d[k] = d[W.Surface];
        }

        foreach (var k in new[] { "TextControlBorderBrush", "ComboBoxBorderBrush" })
        {
            d[k] = d[W.BorderKey];
        }
        // Switch (off): input-border-colored track, card-colored knob; pointer over: the side menu color as track
        foreach (var s in new[] { "", "PointerOver", "Pressed" })
        {
            var over = s != "";
            d["ToggleSwitchFillOff" + s] = over ? d[W.SideKey] : d[W.BorderKey];
            d["ToggleSwitchStrokeOff" + s] = over ? d[W.SideKey] : d[W.BorderKey];
            d["ToggleSwitchKnobFillOff" + s] = d[W.Surface];
        }
        // Check boxes and radio buttons: an outline of their own; every input takes the accent (Rescan button) color under the pointer
        foreach (var s in new[] { "", "PointerOver", "Pressed" })
        {
            var b = s == "" ? B(A(fg, .55)) : d[W.Accent];   // a stronger line than the inputs': a small box or circle on a card must still be seen
            d["CheckBoxCheckBackgroundStrokeUnchecked" + s] = b;
            d["RadioButtonOuterEllipseStroke" + s] = b;
        }
        d["TextControlBorderBrushPointerOver"] = d[W.Accent];
        d["TextControlBorderBrushFocused"] = d[W.Accent];   // Fluent UI 2: the focused input shows the brand color (the same thickness on every side)
        // The date picker looks like the other text inputs: card background, divider-colored border, accent under the pointer
        d["CalendarDatePickerBackground"] = d[W.Surface];
        d["CalendarDatePickerBackgroundPointerOver"] = d[W.Surface];
        d["CalendarDatePickerBackgroundPressed"] = d[W.Surface];
        d["CalendarDatePickerBorderBrush"] = d[W.BorderKey];
        d["CalendarDatePickerBorderBrushPointerOver"] = d[W.Accent];
        d["CalendarDatePickerBorderBrushPressed"] = d[W.Accent];
        // Fluent accent buttons default to white text; each accent here has its own text color.
        foreach (var k in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" })
        {
            d[k] = B(onPrimary);
        }
        // The accent button at rest and the checked check box are the color a side menu item shows under the pointer: the accent, a little away from the text color.
        // Under the pointer the button is 12% lighter in both themes. Text keeps 7.1:1 at rest, under the pointer and pressed (WCAG 2.2 AAA 1.4.6, a custom accent too):
        // where lighter moves toward the text (white text, the light theme) the rest color is deep enough for the lighter hover to pass, and pressed goes 12% darker;
        // where lighter moves away from it (dark text), pressed goes 24% lighter.
        var text = onPrimary.ToUInt32();
        static uint Lighter(uint c) => Services.Contrast.Mix(c, 0xFFFFFFFF, .12);
        var restArgb = Services.Contrast.AtLeast(Shade(primary, onPrimary, .15).ToUInt32(), text, Services.Contrast.Minimum, Lighter);
        var lighterIsAway = Services.Contrast.Ratio(text, Lighter(restArgb)) >= Services.Contrast.Ratio(text, restArgb);
        var fill = B(Color.FromUInt32(restArgb));
        var hovered = B(Color.FromUInt32(Lighter(restArgb)));
        var pressed = B(Color.FromUInt32(Services.Contrast.AwayFrom(restArgb, text, lighterIsAway ? .24 : .12)));
        d["AccentButtonBackground"] = fill;
        d["AccentButtonBackgroundPointerOver"] = hovered;
        d["AccentButtonBackgroundPressed"] = pressed;
        foreach (var (s, brush) in new[] { ("", fill), ("PointerOver", fill), ("Pressed", pressed) })
        {
            d["CheckBoxCheckBackgroundFillChecked" + s] = brush;
            d["CheckBoxCheckBackgroundStrokeChecked" + s] = brush;
            d["CheckBoxCheckGlyphForegroundChecked" + s] = B(onPrimary);   // the check mark has the accent button's text color
        }
        // Drop-downs under the pointer and pressed look like a text box under the pointer: the same card background and text, the accent border
        foreach (var s in new[] { "PointerOver", "Pressed" })
        {
            d["ComboBoxBackground" + s] = d[W.Surface];
            d["ComboBoxBorderBrush" + s] = d[W.Accent];
        }
        return d;
    }

    private static ColorPaletteResources FluentPalette(Palette p)
    {
        Color bg = C(p.Bg), fg = C(p.Fg);
        return new ColorPaletteResources
        {
            Accent = C(p.Primary),
            RegionColor = bg,
            ErrorText = C(p.States.DangerText),
            AltHigh = bg,
            AltMediumHigh = A(bg, .8),
            AltMedium = A(bg, .6),
            AltMediumLow = A(bg, .4),
            AltLow = A(bg, .2),
            BaseHigh = fg,
            BaseMediumHigh = A(fg, .8),
            BaseMedium = A(fg, .6),
            BaseMediumLow = A(fg, .4),
            BaseLow = A(fg, .2),
            ChromeMedium = C(p.Surface),
            ChromeLow = C(p.Surface),
            ChromeAltLow = fg,
        };
    }

    /// <summary>Status text colors on a theme (each at least 7.1:1 on the card and main backgrounds), with those two backgrounds.</summary>
    public static (Color Error, Color Warning, Color Success, Color Background, Color Main) Status(bool dark)
    {
        var p = dark ? _dark : _light;
        return (C(p.States.DangerText), C(p.States.WarningText), C(p.States.SuccessText), C(p.Bg), C(p.Main));
    }

    /// <summary>Status marks (icons, bars, dots) on a theme: at least 3:1 on the card.</summary>
    public static (Color Error, Color Warning, Color Success) Marks(bool dark)
    {
        var s = (dark ? _dark : _light).States;
        return (C(s.DangerMark), C(s.WarningMark), C(s.SuccessMark));
    }

    /// <summary>
    /// Every surface and tint text sits on in a theme: card, main, side, top, a hovered and a selected row on the card and the side,
    /// the selected theme button on the top bar, selected text and a new log line.
    /// </summary>
    public static Color[] TextBackgrounds(bool dark)
    {
        var p = dark ? _dark : _light;
        static Color On(Color top, Color under) => Color.FromUInt32(Services.Contrast.Blend(top.ToUInt32(), under.ToUInt32()));
        Color hover = A(C(p.Fg), p.HoverAlpha), selected = A(C(p.Primary), p.SelectedAlpha);
        return [C(p.Bg), C(p.Main), C(p.Side), C(p.Top), On(hover, C(p.Bg)), On(hover, C(p.Side)), On(selected, C(p.Bg)), On(selected, C(p.Side)), On(selected, C(p.Top)),
            On(A(C(p.Selection), p.SelectionAlpha), C(p.Bg)), On(A(C(Warning), p.HighlightAlpha), C(p.Bg))];
    }

    /// <summary>Text, muted text and link of a theme (the defaults).</summary>
    public static (Color Text, Color Muted, Color Link) Inks(bool dark)
    {
        var p = dark ? _dark : _light;
        return (C(p.Fg), C(p.Muted), C(p.Link));
    }

    /// <summary>A custom accent as link text: moved away from the background just enough to read at 7.1:1 on the card and the main area.</summary>
    private static Color LinkFor(Color accent, Palette p)
    {
        var c = Services.Contrast.AtLeast(accent.ToUInt32(), C(p.Bg).ToUInt32(), Services.Contrast.Minimum);
        return Color.FromUInt32(Services.Contrast.AtLeast(c, C(p.Main).ToUInt32(), Services.Contrast.Minimum));
    }

    /// <summary>Accent fill for hover/pressed: the accent moved away from its text color.</summary>
    public static Color Shade(Color accent, Color text, double amount) =>
        Color.FromUInt32(Services.Contrast.AwayFrom(accent.ToUInt32(), text.ToUInt32(), amount));

    private static Color C(string hex) => Color.Parse(hex);
    private static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);
    private static Avalonia.Media.Immutable.ImmutableSolidColorBrush B(Color c) => new(c);
}
