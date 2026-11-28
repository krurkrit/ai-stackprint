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
/// Design tokens mapped to resource keys used by <see cref="W"/>,
/// plus the Fluent palette so built-in controls match. The only place colors are defined;
/// Appearance settings may override accent, hover and selection per theme.
/// </summary>
internal static partial class Theme
{
    // Color tokens
    private const string White = "#F8F9FA", Black = "#34383C", DarkCard = "#262626";
    private const string BlueLight = "#1450A0", BlueDark = "#74A8F4", OrangeLight = "#8A3F00", OrangeDark = "#FFB066", OrangeAccent = "#FD7E14";
    private const string AccentTextDark = "#141517", Error = "#A52834", Warning = "#FFC107", Success = "#0F7B0F";
    // Text and muted text: the strongest step, and the faintest gray that still reads at 7.1:1 on every surface, hover and selection
    private const string Ink = "#13161A", MutedLight = "#40454A", MutedDark = "#BEC3C9", LinkLight = "#004190";
    // Fills behind white text (danger buttons, Close hover): 7.1:1 at rest; their hover and pressed shades only go darker
    private const string DangerFill = "#96000D";

    /// <summary>
    /// Status colors by formula: fixed hues (danger 27°, warning 80°, success 145° in OKLCH). Text: the most vivid step that reads at 7.1:1 on the card,
    /// the main area, a hovered and a selected row. Marks (icons, bars, dots): 3:1 is enough, so they may be brighter: dark danger is pure red.
    /// Dark danger text is that pure red too, by choice: a strong red cannot reach 7:1 on the dark card, so it reads at 3.7:1 (below AA) and
    /// always comes with an icon or words that say what is wrong.
    /// </summary>
    private sealed record States(string DangerText, string WarningText, string SuccessText, string DangerMark, string WarningMark, string SuccessMark);

    private static readonly States _lightStates = new("#9D010E", "#654702", "#025915", "#9D010E", "#B17E00", "#019D2A");
    private static readonly States _darkStates = new("#FD0105", "#FAB72A", "#58DD64", "#FD0105", "#FAB72A", "#58DD64");

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

    // Areas: top bar, sidebar, main area, and cards/lists/bottom panel/text boxes (Surface = Bg)
    // Main and side are small OKLCH lightness steps below the card, so muted text keeps 7.1:1 on all of them
    private const string LightSide = "#E8EAEC", LightMain = "#EFF1F3", DarkSide = "#1E1F20", DarkMain = "#1A1C1D", DarkTop = "#202123", DarkBorder = "#343434";

    public static readonly string[] LevelNames = ["System", "Local", "Project", "User"];

    // Tints (hover, selected, text selection, new-line highlight): the strongest that keep text and muted text at 7.1:1 on them
    private sealed record Palette(string Bg, string Fg, string Surface, string Primary, string OnPrimary, string Link, string Muted,
        double DividerAlpha, double HoverAlpha, double SelectedAlpha, string Selection, double SelectionAlpha, double HighlightAlpha,
        string Side, string Main, string Top, States States, string? Border = null);

    private static readonly Palette _light = new(White, Ink, White, BlueLight, White, LinkLight, MutedLight, .16, .05, .08, BlueLight, .16, .50, LightSide, LightMain, LightMain, _lightStates);
    private static readonly Palette _dark = new(DarkCard, White, DarkCard, OrangeAccent, AccentTextDark, OrangeDark, MutedDark, .24, .06, .10, BlueDark, .10, .08, DarkSide, DarkMain, DarkTop, _darkStates, DarkBorder);

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

    /// <summary>App categories, each with the chart slot of its place here (1 to 7), whatever the chart shows.</summary>
    public static readonly string[] Categories = ["agent", "chat", "editor", "orchestrator", "runtime", "shared", "custom"];

    /// <summary>A chart color: the exact hex, and its OKLCH rounded (L to .01, C to .001, H to 1°); ThemeTests keeps the two in step.</summary>
    public sealed record ChartColor(string Hex, double Lightness, double Chroma, double Hue);

    /// <summary>
    /// The chart palette: 8 series colors per theme (light on #F8F9FA, dark on #262626). Slot N is series N: a chart takes them in order and never
    /// reorders or cycles them; past 8 series the rest are "Other" in muted gray, never a new hue. Light slot 5 is 3.1:1 on the card: never text.
    /// </summary>
    public static readonly ChartColor[] LightChart =
    [
        new("#1958BB", .48, .169, 260), new("#994403", .49, .131, 49), new("#056A3E", .46, .109, 156), new("#7C4096", .48, .144, 315),
        new("#BC8302", .65, .135, 78), new("#058292", .56, .095, 210), new("#C02658", .53, .190, 8), new("#747D00", .56, .127, 115)
    ];

    public static readonly ChartColor[] DarkChart =
    [
        new("#F7791A", .71, .180, 50), new("#42ACFF", .72, .154, 246), new("#42DC9A", .80, .159, 160), new("#C888E3", .72, .144, 315),
        new("#F8C739", .85, .158, 88), new("#45C5CE", .76, .110, 202), new("#FC647B", .70, .186, 14), new("#ADBE5A", .77, .127, 118)
    ];

    /// <summary>The color of chart slot <paramref name="slot"/> (1 to 8) in a theme.</summary>
    public static Color Chart(bool dark, int slot) => C((dark ? DarkChart : LightChart)[slot - 1].Hex);

    /// <summary>Default level color (System, Local, Project, User): chart slots 1 to 4.</summary>
    public static Color LevelDefault(bool dark, int i) => Chart(dark, i + 1);

    /// <summary>Default colors shown as placeholders in Appearance: (accent, hover, selection) for a theme.</summary>
    public static (Color Accent, Color Hover, Color Selection, Color Background, Color Text) Defaults(bool dark)
    {
        var p = dark ? _dark : _light;
        return (C(p.Primary), A(C(p.Fg), p.HoverAlpha), A(C(p.Selection), p.SelectionAlpha), C(p.Bg), C(p.Fg));
    }

    /// <summary>Applies the Appearance settings: theme, fonts, sizes, weights and color overrides. Views read sizes and weights from <see cref="W"/>.</summary>
    public static void Appearance(Application app, Settings s)
    {
        Motion.Enabled = s.Animations;
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

    /// <summary>
    /// The shadow of a dialog card: Fluent UI 2 elevation 16, the same as a pop-up. A dialog is its own transparent window with
    /// <see cref="Sizes.ShadowRoom"/> around the card, and the whole shadow must fit in it: elevation 64 (32 down, 64 blur) was cut off at
    /// the window's edge and showed as hard bands on the left, right and bottom.
    /// </summary>
    public static Avalonia.Media.BoxShadows DialogShadow(bool dark) => PopupShadow(dark);

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
