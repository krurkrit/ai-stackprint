// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
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
        return new()
        {
            Palettes =
            {
                [ThemeVariant.Light] = _lightFluent,
                [ThemeVariant.Dark] = _darkFluent
            }
        };
    }

    private static FluentAvalonia.Styling.FluentAvaloniaTheme? _controls;

    /// <summary>Fluent UI 2 control templates (FluentAvalonia) over the built-in theme; our accent and palette stay.</summary>
    public static FluentAvalonia.Styling.FluentAvaloniaTheme Controls() =>
        _controls = new()
        {
            PreferSystemTheme = false,
            PreferUserAccentColor = false,
            CustomAccentColor = C(_light.Primary)
        };

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
        var f = new FontRuleSet(s.Fonts);
        ResolvedFont body = f.Resolve(FontRoles.Body), head = f.Resolve(FontRoles.Heading), log = f.Resolve(FontRoles.Log);
        Font = Stack(body.Family);
        Mono = MonoStack(log.Family);
        MonoSize = log.Size;
        MonoWeight = (FontWeight)log.Weight;
        ResolvedFont emphasis = f.Resolve(FontRoles.Emphasis), menu = f.Resolve(FontRoles.Menu);
        W.HeadingFont = head.Family == body.Family ? null : Stack(head.Family);
        W.EmphasisFont = emphasis.Family == body.Family ? null : Stack(emphasis.Family);
        W.MenuFont = menu.Family == body.Family ? null : Stack(menu.Family);
        W.EmphasisScale = emphasis.Size / body.Size;
        W.HeadingScale = head.Size / body.Size;
        W.MenuScale = menu.Size / body.Size;
        W.HeadingWeight = (FontWeight)head.Weight;
        W.MenuWeight = (FontWeight)menu.Weight;
        app.Resources["ContentControlThemeFontFamily"] = Font;
        W.Scale = body.Size / 14.0;
        app.Resources["ControlContentThemeFontSize"] = W.Size(14);
        app.Resources["TabItemHeaderFontSize"] = W.Size(TextSize.Body);   // Fluent UI 2 tabs are body size (the control default is 28)
        Inputs(app, W.Size(14));
        W.Body = (FontWeight)body.Weight;
        W.Emphasis = (FontWeight)emphasis.Weight;
        _controls?.CustomAccentColor = ParseHex(s.Light.Accent) ?? C(_light.Primary);   // its light fills are fixed when it loads

        app.Resources.ThemeDictionaries[ThemeVariant.Light] = Keys(_light, s.Light, _lightFluent);
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = Keys(_dark, s.Dark, _darkFluent);
    }

    /// <summary>The shadow of a dialog card (Fluent UI 2 elevation 64: a soft close shadow and a deep wide one); stronger on the dark theme.</summary>
    public static Avalonia.Media.BoxShadows DialogShadow(bool dark) => new(
        new Avalonia.Media.BoxShadow
        {
            OffsetX = 0,
            OffsetY = 0,
            Blur = 8,
            Color = Avalonia.Media.Color.FromArgb(dark ? (byte)0x66 : (byte)0x33, 0, 0, 0)
        },
        [new Avalonia.Media.BoxShadow
        {
            OffsetX = 0,
            OffsetY = 32,
            Blur = 64,
            Color = Avalonia.Media.Color.FromArgb(dark ? (byte)0x80 : (byte)0x3D, 0, 0, 0)
        }]);

    /// <summary>The shadow of a pop-up that floats over the page (Fluent UI 2 elevation 16): the command palette and toasts.</summary>
    public static Avalonia.Media.BoxShadows PopupShadow(bool dark) => new(
        new Avalonia.Media.BoxShadow
        {
            OffsetX = 0,
            OffsetY = 0,
            Blur = 2,
            Color = Avalonia.Media.Color.FromArgb(dark ? (byte)0x66 : (byte)0x1F, 0, 0, 0)
        },
        [new Avalonia.Media.BoxShadow
        {
            OffsetX = 0,
            OffsetY = 8,
            Blur = 16,
            Color = Avalonia.Media.Color.FromArgb(dark ? (byte)0x80 : (byte)0x24, 0, 0, 0)
        }]);

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Parses #RRGGBB / #AARRGGBB; null when empty or invalid.</summary>
    public static Color? ParseHex(string? hex) => !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var c) ? c : null;
}
