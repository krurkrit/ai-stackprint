// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace AiStackprint.Ui;

/// <summary>
/// Snowy Owl design tokens (snowy-owl/docs/TOKENS.md) mapped to resource keys used by <see cref="W"/>,
/// plus the Fluent palette so built-in controls match. The only place colors are defined;
/// Appearance settings may override accent, hover and selection per theme.
/// </summary>
internal static partial class Theme
{
    // Snowy Owl color tokens
    private const string White = "#F8F9FA", Black = "#34383C", DarkBlack = "#18191B";
    private const string BlueLight = "#1450A0", BlueDark = "#74A8F4", OrangeLight = "#8A3F00", OrangeDark = "#FFB066", OrangeAccent = "#FD7E14";
    private const string AccentTextDark = "#141517", Error = "#A52834", Warning = "#FFC107", Success = "#12633D";
    // Status colors used as text, icons and bars must read on each theme's background: light warning is the Snowy Owl brown,
    // dark error and success are lighter tints. Fills behind white text (danger buttons, Close hover) keep Error.
    private const string ErrorDark = "#F28B95", SuccessDark = "#5FCB94";

    /// <summary>Swatches offered in Appearance › Colors.</summary>
    public static readonly (string Name, string Hex)[] Swatches =
    [
        ("Blue", BlueLight), ("Light blue", BlueDark), ("Orange", OrangeAccent), ("Brown", OrangeLight), ("Peach", OrangeDark),
        ("Green", Success), ("Red", Error), ("Yellow", Warning), ("Graphite", Black), ("Paper", White)
    ];

    /// <summary>UI font stack: the chosen family (default Inter), then Noto Sans, then the platform default. Fonts are not bundled.</summary>
    public static FontFamily Font { get; private set; } = Stack("Inter");
    /// <summary>Logs and the config editor: the chosen monospace family, then common fallbacks.</summary>
    public static FontFamily Mono { get; private set; } = MonoStack("JetBrains Mono");
    public static double MonoSize { get; private set; } = 12.5;
    public static FontWeight MonoWeight { get; private set; } = FontWeight.Normal;

    // Areas: sidebar, main area, and cards/lists/bottom panel/text boxes (Surface = Bg)
    private const string LightSide = "#DEE2E6", LightMain = "#E9ECEF", DarkSide = "#2E3035", DarkMain = "#24262A";

    /// <summary>Default level colors (System, Local, Project, User) for the dashboard bars.</summary>
    public static readonly string[] LightLevels = ["#7048E8", "#D6336C", "#2F9E44", "#1098AD"], DarkLevels = ["#9775FA", "#F06595", "#51CF66", "#3BC9DB"];
    public static readonly string[] LevelNames = ["System", "Local", "Project", "User"];

    private sealed record Palette(string Bg, string Fg, string Surface, string Primary, string OnPrimary, string Link,
        double MutedAlpha, double DividerAlpha, double HoverAlpha, double SelectedAlpha, string[] Levels, string Selection, double SelectionAlpha, string Side, string Main);

    private static readonly Palette _light = new(White, Black, White, BlueLight, White, BlueLight, .76, .16, .08, .12, LightLevels, BlueLight, .25, LightSide, LightMain);   // selection 25%: text keeps >= 7.1:1
    private static readonly Palette _dark = new(DarkBlack, White, DarkBlack, OrangeAccent, AccentTextDark, OrangeDark, .72, .24, .10, .14, DarkLevels, BlueDark, .35, DarkSide, DarkMain);

    // Made with each theme: a palette belongs to one application (headless tests start one per test)
    private static ColorPaletteResources _lightFluent = FluentPalette(_light), _darkFluent = FluentPalette(_dark);

    public static FluentTheme Fluent()
    {
        _lightFluent = FluentPalette(_light);
        _darkFluent = FluentPalette(_dark);
        return new() { Palettes = { [ThemeVariant.Light] = _lightFluent, [ThemeVariant.Dark] = _darkFluent } };
    }

    private static FontFamily Stack(string family) => new(family == "Inter" ? "Inter, Noto Sans, $Default" : $"{family}, Inter, Noto Sans, $Default");
    private static FontFamily MonoStack(string family) => new($"{family}, JetBrains Mono, Cascadia Mono, Consolas, Menlo, monospace");

    /// <summary>The two text colors an accent button can use; the app picks whichever reads better.</summary>
    public static (Color Light, Color Dark) AccentTexts => (C(White), C(AccentTextDark));

    /// <summary>App category colors for charts (Okabe-Ito based, checked against the card color of each theme: every slice is at least 3:1 and none look alike for color-blind viewers).</summary>
    public static readonly string[] Categories = ["agent", "chat", "editor", "orchestrator", "runtime", "shared", "custom"];
    private static readonly string[] _lightCategory = ["#1F6FB5", "#D1590E", "#178A5B", "#8E4A9E", "#9A7400", "#5F6B76", "#C2306E"];
    private static readonly string[] _darkCategory = ["#5AA9F0", "#FF8C42", "#4CC38A", "#C58BE0", "#E8B923", "#9AA5B1", "#F06595"];

    private static readonly double[] _levelAlpha = [1, .85, .72, .62];   // faintest still >= 3:1 on the card (chart marks)

    /// <summary>Default level color: the accent, fainter for each level after System (opaque, blended onto the card color so stacked bars stay solid).</summary>
    public static Color LevelDefault(bool dark, int i, Color? accent = null)
    {
        var p = dark ? _dark : _light;
        var a = accent ?? C(p.Primary);
        return Color.FromUInt32(Services.Contrast.Blend(A(a, _levelAlpha[i]).ToUInt32(), C(p.Bg).ToUInt32()));
    }

    /// <summary>Default colors shown as placeholders in Appearance: (accent, hover, selection) for a theme.</summary>
    public static (Color Accent, Color Hover, Color Selection, Color Background, Color Text) Defaults(bool dark)
    {
        var p = dark ? _dark : _light;
        return (C(p.Primary), A(C(p.Fg), p.HoverAlpha), A(C(p.Selection), p.SelectionAlpha), C(p.Bg), C(p.Fg));
    }

    /// <summary>Applies the Appearance settings: theme, fonts, sizes, weights and color overrides. Views read sizes and weights from <see cref="W"/>.</summary>
    public static void Appearance(Application app, Settings s)
    {
        app.RequestedThemeVariant = s.Theme switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        Font = Stack(string.IsNullOrWhiteSpace(s.FontFamily) ? "Inter" : s.FontFamily);
        Mono = MonoStack(string.IsNullOrWhiteSpace(s.LogFontFamily) ? "JetBrains Mono" : s.LogFontFamily);
        MonoSize = Math.Clamp(s.LogFontSize, 10, 18);
        MonoWeight = (FontWeight)Weight(s.LogFontWeight, 400);
        W.HeadingFont = string.IsNullOrWhiteSpace(s.HeadingFontFamily) ? null : Stack(s.HeadingFontFamily);
        W.HeadingWeight = s.HeadingWeight is 300 or 400 or 500 or 600 or 700 ? (FontWeight)s.HeadingWeight : null;
        W.MenuWeight = (FontWeight)Weight(s.MenuWeight, 500);
        app.Resources["ContentControlThemeFontFamily"] = Font;
        W.Scale = Math.Clamp(s.FontSize, 12, 18) / 14.0;
        app.Resources["ControlContentThemeFontSize"] = W.Size(14);
        Inputs(app, W.Size(14));
        W.Body = (FontWeight)Weight(s.BodyWeight, 400);
        W.Emphasis = (FontWeight)Weight(s.EmphasisWeight, 600);
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = Keys(_light, s.Light, _lightFluent);
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = Keys(_dark, s.Dark, _darkFluent);
    }

    private static int Weight(int w, int fallback) => w is 300 or 400 or 500 or 600 or 700 ? w : fallback;

    /// <summary>Parses #RRGGBB / #AARRGGBB; null when empty or invalid.</summary>
    public static Color? ParseHex(string? hex) => !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var c) ? c : null;

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
            [W.Muted] = B(A(fg, p.MutedAlpha)),
            [W.BorderKey] = B(A(fg, p.DividerAlpha)),
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
            [W.WarningKey] = B(Status(p == _dark).Warning),
            [W.SuccessKey] = B(Status(p == _dark).Success),
            // Status outline (Snowy Owl report style): success border, tinted fill; dark theme keeps normal text for contrast
            [W.SuccessText] = B(p == _dark ? fg : C(Success)),
            [W.SuccessFill] = B(A(Status(p == _dark).Success, .12)),
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
        for (var i = 0; i < Categories.Length; i++)
        {
            d[W.CategoryKey(Categories[i])] = B(C((p == _dark ? _darkCategory : _lightCategory)[i]));
        }

        for (var i = 0; i < LevelNames.Length; i++)
        {
            d[W.LevelKey(LevelNames[i])] = B(ParseHex(o.Levels?.ElementAtOrDefault(i)) ?? LevelDefault(p == _dark, i, primary));
        }
        // Inputs sit on cards: card background and the divider color as border (cards themselves have no border)
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed" })
        {
            d[k] = d[W.Surface];
        }

        foreach (var k in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed" })
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
        // Check boxes and radio buttons: the input border color; every input takes the accent (Rescan button) color under the pointer
        foreach (var s in new[] { "", "PointerOver", "Pressed" })
        {
            var b = s == "" ? d[W.BorderKey] : d[W.Accent];
            d["CheckBoxCheckBackgroundStrokeUnchecked" + s] = b;
            d["RadioButtonOuterEllipseStroke" + s] = b;
        }
        d["TextControlBorderBrushPointerOver"] = d[W.Accent];
        d["ComboBoxBorderBrushPointerOver"] = d[W.Accent];
        d["ComboBoxBorderBrushPressed"] = d[W.Accent];
        // Fluent accent buttons default to white text; Snowy Owl pairs each accent with its own text color.
        foreach (var k in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" })
        {
            d[k] = B(onPrimary);
        }
        // Accent hover and pressed: darker or lighter, away from the text color, so the text never loses contrast (Fluent's own hover could drop below 4.5:1)
        d["AccentButtonBackgroundPointerOver"] = B(Shade(primary, onPrimary, .15));
        d["AccentButtonBackgroundPressed"] = B(Shade(primary, onPrimary, .28));
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
        ? (C(ErrorDark), C(Warning), C(SuccessDark), C(DarkBlack), C(DarkMain))
        : (C(Error), C(OrangeLight), C(Success), C(White), C(LightMain));

    /// <summary>Chart series after the accent: [0] is the default accent, then output, cache write, cache read. Dark output is blue because the dark accent is orange.</summary>
    public static Color[] Series(bool dark) => dark
        ? [C(OrangeAccent), C(BlueDark), C("#5FCB94"), C("#9AA0A6")]
        : [C(BlueLight), C("#D1590E"), C(Success), C("#7A8087")];

    /// <summary>Accent fill for hover/pressed: the accent moved away from its text color.</summary>
    public static Color Shade(Color accent, Color text, double amount) =>
        Color.FromUInt32(Services.Contrast.AwayFrom(accent.ToUInt32(), text.ToUInt32(), amount));

    private static Color C(string hex) => Color.Parse(hex);
    private static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);
    private static Avalonia.Media.Immutable.ImmutableSolidColorBrush B(Color c) => new(c);
}
