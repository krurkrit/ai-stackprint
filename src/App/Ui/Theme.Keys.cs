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
            [W.Muted] = B(A(fg, p.MutedAlpha)),
            [W.BorderKey] = B(p.Border == null ? A(fg, p.DividerAlpha) : C(p.Border)),   // dark: a solid line
            [W.Subtle] = B(hover),
            [W.Selected] = B(A(primary, p.SelectedAlpha)),
            [W.Accent] = B(primary),
            [W.OnAccent] = B(onPrimary),
            [W.Link] = B(custom ?? C(p.Link)),
            [W.SelectionKey] = B(selection),
            ["TextControlSelectionHighlightColor"] = B(selection),
            [W.ErrorKey] = B(Status(p == _dark).Error),
            [W.ErrorFill] = B(C(Error)),
            [W.OnError] = B(Colors.White),
            [W.ErrorHover] = B(Shade(C(Error), Colors.White, .15)),
            [W.ErrorPressed] = B(Shade(C(Error), Colors.White, .28)),
            [W.WarningKey] = B(Status(p == _dark).Warning),
            [W.SuccessKey] = B(Status(p == _dark).Success),
            // The "Installed" status: plain green text, no pill (dark theme #6CCB5F, light theme the darker green)
            [W.SuccessText] = B(Status(p == _dark).Success),
            // Chart series: four hues that differ in both themes (accent, then orange or light blue, green, gray)
            [W.ChartIn] = B(primary),   // first series follows the accent
            [W.ChartOut] = B(Series(p == _dark)[1]),
            [W.ChartWrite] = B(Series(p == _dark)[2]),
            [W.ChartRead] = B(Series(p == _dark)[3]),
            ["ButtonBackgroundPointerOver"] = B(hover),
            // New log lines/steps, fading out: amber fill behind the text and a solid ring (light: orange, dark: yellow)
            [W.HighlightKey] = B(A(C(Warning), p == _dark ? .26 : .42)),
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

        for (var i = 0; i < Categories.Length; i++)
        {
            d[W.CategoryKey(Categories[i])] = B(C((p == _dark ? _darkCategory : _lightCategory)[i]));
        }

        for (var i = 0; i < LevelNames.Length; i++)
        {
            d[W.LevelKey(LevelNames[i])] = B(ParseHex(o.Levels?.ElementAtOrDefault(i)) ?? LevelDefault(p == _dark, i, primary));
        }
        // Inputs sit on cards: card background and the divider color as border (cards themselves have no border)
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundFocused", "ComboBoxBackground" })
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
        d["TextControlBorderBrushFocused"] = d[W.Accent];   // Fluent UI 2: the focused input shows the brand color (the same thickness on every side)
        // The date picker looks like the other inputs: card background, divider-colored border
        d["CalendarDatePickerBackground"] = d[W.Surface];
        d["CalendarDatePickerBorderBrush"] = d[W.BorderKey];
        // Fluent accent buttons default to white text; each accent here has its own text color.
        foreach (var k in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" })
        {
            d[k] = B(onPrimary);
        }
        // The accent button and the checked check box are always the color a side menu item shows under the pointer (the accent, a little away from the text color: the text never loses contrast)
        var fill = B(Shade(primary, onPrimary, .15));
        var pressed = B(Shade(primary, onPrimary, .28));
        d["AccentButtonBackground"] = fill;
        d["AccentButtonBackgroundPointerOver"] = fill;
        d["AccentButtonBackgroundPressed"] = pressed;
        foreach (var (s, brush) in new[] { ("", fill), ("PointerOver", fill), ("Pressed", pressed) })
        {
            d["CheckBoxCheckBackgroundFillChecked" + s] = brush;
            d["CheckBoxCheckBackgroundStrokeChecked" + s] = brush;
            d["CheckBoxCheckGlyphForegroundChecked" + s] = B(onPrimary);   // the check mark has the accent button's text color
        }
        // Inputs under the pointer look like a side menu item under the pointer: the accent fill and its text color, a little darker while pressed (a focused text box keeps its own look while typing)
        foreach (var (s, brush) in new[] { ("PointerOver", fill), ("Pressed", pressed) })
        {
            foreach (var k in new[] { "ComboBoxBackground", "ComboBoxBorderBrush", "CalendarDatePickerBackground", "CalendarDatePickerBorderBrush" })
            {
                d[k + s] = brush;
            }

            d["ComboBoxForeground" + s] = B(onPrimary);
        }

        d["TextControlBackgroundPointerOver"] = fill;
        d["TextControlBorderBrushPointerOver"] = fill;
        foreach (var k in new[] { "TextControlForegroundPointerOver", "CalendarDatePickerTextForegroundPointerOver", "CalendarDatePickerCalendarGlyphForegroundPointerOver" })
        {
            d[k] = B(onPrimary);
        }

        foreach (var k in new[] { "TextControlPlaceholderForegroundPointerOver", "ComboBoxPlaceHolderForegroundPointerOver" })
        {
            d[k] = B(A(onPrimary, .8));
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
            ErrorText = C(Error),
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

    /// <summary>Status colors for text, icons and bars on a theme (each at least 4.5:1 on the card and main backgrounds).</summary>
    public static (Color Error, Color Warning, Color Success, Color Background, Color Main) Status(bool dark) => dark
        ? (C(ErrorDark), C(Warning), C(SuccessDark), C(DarkCard), C(DarkMain))
        : (C(Error), C(OrangeLight), C(Success), C(White), C(LightMain));

    /// <summary>Chart series after the accent: [0] is the default accent, then output, cache write, cache read. Dark output is blue because the dark accent is orange.</summary>
    public static Color[] Series(bool dark) => dark
        ? [C(OrangeAccent), C(BlueDark), C("#6CCB5F"), C("#9AA0A6")]
        : [C(BlueLight), C("#D1590E"), C(Success), C("#7A8087")];

    /// <summary>Accent fill for hover/pressed: the accent moved away from its text color.</summary>
    public static Color Shade(Color accent, Color text, double amount) =>
        Color.FromUInt32(Services.Contrast.AwayFrom(accent.ToUInt32(), text.ToUInt32(), amount));

    private static Color C(string hex) => Color.Parse(hex);
    private static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);
    private static Avalonia.Media.Immutable.ImmutableSolidColorBrush B(Color c) => new(c);
}
