// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>Small widget factory. Colors are resource keys defined in <see cref="Theme"/>, so light/dark work automatically.</summary>
internal static partial class W
{
    // Resource keys (values in Theme)
    public const string Background = "Ap.Background", Text = "Ap.Text", Muted = "Ap.Muted", Surface = "Ap.Surface", SideKey = "Ap.Side", MainKey = "Ap.Main";
    public const string Subtle = "Ap.Hover", Selected = "Ap.Selected", BorderKey = "Ap.Divider";
    public const string Accent = "Ap.Accent", OnAccent = "Ap.OnAccent", Link = "Ap.Link";
    public const string ErrorKey = "Ap.Error", ErrorFill = "Ap.ErrorFill", OnError = "Ap.OnError", WarningKey = "Ap.Warning", SuccessKey = "Ap.Success";
    public const string ChartIn = "Ap.ChartIn", ChartOut = "Ap.ChartOut", ChartWrite = "Ap.ChartWrite", ChartRead = "Ap.ChartRead";
    public const string SuccessText = "Ap.SuccessText", SuccessFill = "Ap.SuccessFill", SelectionKey = "Ap.Selection", HighlightKey = "Ap.Highlight", HighlightRingKey = "Ap.HighlightRing", ScrimKey = "Ap.Scrim";

    private static uint Argb(Color c) => c.ToUInt32();
    /// <summary>WCAG contrast of <paramref name="fg"/> (blended if translucent) on an opaque <paramref name="bg"/>.</summary>
    public static double ContrastRatio(Color fg, Color bg) => Services.Contrast.Ratio(Argb(fg), Argb(bg));
    /// <summary>Whichever of two text colors reads better on <paramref name="bg"/>.</summary>
    public static Color BestText(Color bg, Color a, Color b) => ContrastRatio(a, bg) >= ContrastRatio(b, bg) ? a : b;

    // Appearance (set by Theme.Appearance from Settings). Sizes in code are written for a 14 px base and scaled.
    public static double Scale { get; set; } = 1;
    public static FontWeight Body { get; set; } = FontWeight.Normal;
    public static FontWeight Emphasis { get; set; } = FontWeight.Medium;
    /// <summary>Headings: one step above emphasis, at least SemiBold.</summary>
    public static FontWeight Heading => HeadingWeight ?? (Emphasis >= FontWeight.SemiBold ? FontWeight.Bold : FontWeight.SemiBold);
    public static FontWeight? HeadingWeight { get; set; }
    public static FontFamily? HeadingFont { get; set; }
    public static FontWeight MenuWeight { get; set; } = FontWeight.Medium;

    /// <summary>Numbers only (digits and separators): aligned digits in the body font.</summary>
    private static void NumberStyle(TextBlock t, string s)
    {
        if (s.Length == 0 || s.Length > 24 || !s.All(c => char.IsDigit(c) || c is ',' or '.' or ' ' or '%' or ':' or '-' or '/'))
        {
            return;
        }

        t.FontFeatures = FontFeatureCollection.Parse("tnum");
    }
    public static double Size(double px) => Math.Round(px * Scale, 1);
    /// <summary>Chart color of an app category; unknown categories reuse the palette by a stable hash of the name.</summary>
    public static string CategoryKey(string category) =>
        "Ap.Cat." + (Array.IndexOf(Theme.Categories, category) >= 0 ? category : Theme.Categories[category.Aggregate(0, (h, ch) => (h * 31 + ch) & 0x7fffffff) % Theme.Categories.Length]);
    public static string LevelKey(string level) => "Ap.Level." + level;

    public static T Themed<T>(this T c, AvaloniaProperty p, string key) where T : Control
    { c.Bind(p, c.GetResourceObservable(key)); return c; }

    public static T With<T>(this T t, Action<T> a) { a(t); return t; }

    /// <summary>Detaches a long-lived control from its old page so a rebuilt page can host it again.</summary>
    public static T Free<T>(this T c) where T : Control
    {
        switch (c.Parent)
        {
            case Panel p: p.Children.Remove(c); break;
            case ContentControl cc: cc.Content = null; break;
            case Decorator d: d.Child = null; break;
        }
        return c;
    }

    /// <summary>Text at a 14-px-based size; bold = the Emphasis weight. It can be selected by dragging and copied, except inside a button or a clickable row (<see cref="CopyableText"/>).</summary>
    public static TextBlock Txt(string s, double size = TextSize.Body, bool bold = false, string key = Text) =>
        new CopyableText { Text = s, FontSize = Size(size), FontWeight = bold ? Emphasis : Body, TextWrapping = TextWrapping.Wrap }.With(t => NumberStyle(t, s))
            .Themed(TextBlock.ForegroundProperty, key);

    /// <summary>Like <see cref="Txt"/> but the text can be selected and copied by dragging. Only for text that is not part of a click target.</summary>
    public static SelectableTextBlock Sel(string s, double size = TextSize.Body, bool bold = false, string key = Text) =>
        new SelectableTextBlock { Text = s, FontSize = Size(size), FontWeight = bold ? Heading : Body, TextWrapping = TextWrapping.Wrap }
            .Themed(TextBlock.ForegroundProperty, key);

    /// <summary>Card titles, names in headers, big numbers: Heading weight.</summary>
    public static TextBlock Title(string s, double size = TextSize.Subtitle) =>
        new CopyableText { Text = s, FontSize = Size(size), FontWeight = Heading, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }
            .With(t => { if (HeadingFont != null) { t.FontFamily = HeadingFont; } NumberStyle(t, s); });

    public static TextBlock Mono(string s, double size = TextSize.Small) => Txt(s, size).With(t => t.FontFamily = Theme.Mono);

    /// <summary>Set by the main window: navigation for clickable breadcrumb parts.</summary>
    public static Action<string>? GoAction { get; set; }

    /// <summary>Page title. Back/forward live in the window title bar.</summary>
    /// <summary>
    /// The row of a page title with its buttons or filters on the right. It is as tall as a button on every page, so the title sits at the same height whether the page has a button,
    /// a row of pills or nothing at the right.
    /// </summary>
    public static DockPanel Head() => new() { MinHeight = Theme.InputHeight };

    public static Control H1(string s) => Title(s, TextSize.Page);

    /// <summary>Detail-page title "Parent › Name"; the parent is muted and clickable when <paramref name="go"/> is a view.</summary>
    public static Control H1(string parent, string? go, string name)
    {
        var p = Title(parent, TextSize.Page).Themed(TextBlock.ForegroundProperty, Muted);
        if (go != null) { p.Cursor = Hand; p.PointerReleased += (_, _) => GoAction?.Invoke(go); }
        var sep = Title("›", TextSize.Page).Themed(TextBlock.ForegroundProperty, Muted);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.L, Children = { p, sep, Title(name, TextSize.Page) } };
    }

    public static Border Card(Control c) =>
        new Border { CornerRadius = new CornerRadius(Radius.Card), Padding = new Thickness(Space.XXL), Child = c }
            .Themed(Border.BackgroundProperty, Surface);

    /// <summary>The surface of a page that is only a table: the card color with a smaller corner (no icon header, so no concentric corner) and a little padding.</summary>
    public static Border TableSurface(Control table) =>
        new Border { CornerRadius = new CornerRadius(Radius.L), Padding = new Thickness(Space.S, Space.L, Space.S, Space.S), Child = table }
            .Themed(Border.BackgroundProperty, Surface);

    /// <summary>Card with the standard header and <paramref name="body"/> below it.</summary>
    public static Border Section(string icon, string title, Control body, params Control[] actions)
    {
        // The body fills the rest of the card, so a card stretched by its row can center its content
        // The header row is one input tall with the icon tile centered in it: it moves up by the difference, so the tile is as far from the top as from the left
        var lift = Math.Min(0, (Sizes.IconTile - Theme.InputHeight) / 2);
        var head = CardHeader(icon, title, actions).With(h => { DockPanel.SetDock(h, Dock.Top); h.Margin = new Thickness(0, lift, 0, Space.L); });
        return Card(new DockPanel { Children = { head, body } });
    }

    /// <summary>
    /// The one card header: icon on a tinted square, title, optional buttons on the right. Every card uses it, so headers line up
    /// across a row of cards: the title row is one input tall whether or not it has buttons.
    /// </summary>
    public static Control CardHeader(string icon, string title, params Control[] actions)
    {
        // Title keeps its width; the actions wrap onto more lines (right-aligned) when the card is narrow, instead of running over the title
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = Space.XL };
        var name = Row(IconTile(icon), Title(title, TextSize.Subtitle)).With(r => { r.VerticalAlignment = VerticalAlignment.Top; r.Height = Theme.InputHeight; });
        var btns = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = Space.M, LineSpacing = Space.M, VerticalAlignment = VerticalAlignment.Top };
        btns.Children.AddRange(actions);
        Grid.SetColumn(btns, 1);
        head.Children.Add(name); head.Children.Add(btns);
        return head;
    }

    /// <summary>Scrollable page with the standard margins.</summary>
    public static ScrollViewer Page(params Control[] children)
    {
        var sp = new StackPanel { Spacing = Space.XXL, Margin = new Thickness(Space.Page, Space.XXXL) };
        sp.Children.AddRange(children);
        return new ScrollViewer { Content = sp };
    }

    public static Control Dot(string key) =>
        new Border { Width = Sizes.Dot, Height = Sizes.Dot, CornerRadius = new CornerRadius(Radius.Pill), VerticalAlignment = VerticalAlignment.Center }.Themed(Border.BackgroundProperty, key);

    /// <summary>Hollow dot: Local level (personal, same color family as Project).</summary>
    private static Border Ring(string key) =>
        new Border { Width = Sizes.Dot, Height = Sizes.Dot, CornerRadius = new CornerRadius(Radius.Pill), BorderThickness = new Thickness(Stroke.Medium), VerticalAlignment = VerticalAlignment.Center }.Themed(Border.BorderBrushProperty, key);

    public static Control LevelDot(string level) => level == "Local" ? Ring(LevelKey(level)) : Dot(LevelKey(level));

    /// <summary>Success status as an outlined pill (Snowy Owl report style). Readable in both themes.</summary>
    public static Control Status(string s) =>
        new Border
        {
            CornerRadius = new CornerRadius(Radius.Pill),
            BorderThickness = new Thickness(Stroke.Thin),
            Padding = new Thickness(Space.M, Space.Hair),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Txt(s, TextSize.Caption, true, SuccessText)
        }
            .Themed(Border.BorderBrushProperty, SuccessKey).Themed(Border.BackgroundProperty, SuccessFill);

    public static TextBlock DotText(string s, string key) => WithDot(Dot(key), s, TextSize.Small).With(t => t.VerticalAlignment = VerticalAlignment.Center);

    /// <summary>
    /// Dot as the first inline of the text, standing on the baseline, so it lines up with the letters at any font or size.
    /// A dot beside a TextBlock is centered on the line box, which sits higher than the letters.
    /// </summary>
    public static TextBlock WithDot(Control dot, string s, double size, bool bold = false)
    {
        var t = Txt("", size, bold).With(x => { x.Text = null; x.TextWrapping = TextWrapping.NoWrap; });
        dot.Margin = new Thickness(0, 0, Space.S, 0);
        t.Inlines ??= [];
        t.Inlines.Add(new InlineUIContainer(dot) { BaselineAlignment = BaselineAlignment.Baseline });
        t.Inlines.Add(new Run(s));
        return t;
    }

    /// <summary>Level with its dot; tooltip shows the app's own word for it when it differs.</summary>
    public static Control Level(string level, string? text = null, string? appWord = null)
    {
        // One TextBlock: it gets the column width and trims instead of overlapping the next column
        var t = (LevelDots ? WithDot(LevelDot(level), text ?? level, TextSize.Small) : Txt(text ?? level, TextSize.Small).With(x => x.TextWrapping = TextWrapping.NoWrap)).With(x => { x.TextTrimming = TextTrimming.CharacterEllipsis; x.VerticalAlignment = VerticalAlignment.Center; });
        var word = appWord != null && !appWord.Equals(level, StringComparison.OrdinalIgnoreCase) ? $"\nCalled \"{appWord}\" by this app" : "";
        ToolTip.SetTip(t, Levels.Describe(level) + word);
        return t;
    }

    public static Control Chip(string s, string? dotKey = null)
    {
        Control body = dotKey == null || (!LevelDots && dotKey.StartsWith("Ap.Level.")) ? Txt(s, TextSize.Caption, true) : WithDot(dotKey.StartsWith("Ap.Level.") ? LevelDot(dotKey[9..]) : Dot(dotKey), s, TextSize.Caption, true);
        return new Border { CornerRadius = new CornerRadius(Radius.Pill), Padding = new Thickness(Space.L, Space.XS), Child = body, HorizontalAlignment = HorizontalAlignment.Left }
            .Themed(Border.BackgroundProperty, Subtle);
    }

    /// <summary>Vector icon; fills with the inherited foreground so it follows text color, buttons and theme.</summary>
    public static Control Icon(string name, double size = IconSize.M) => Glyph(Icons.Get(name), size);

    private static Viewbox Glyph(Icons.Shape[] shapes, double size)
    {
        var canvas = new Canvas { Width = Sizes.IconBox, Height = Sizes.IconBox };
        foreach (var s in shapes)
        {
            var p = new Avalonia.Controls.Shapes.Path { Data = s.Data, Opacity = s.Opacity };
            p.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, p.GetObservable(TextElement.ForegroundProperty));
            canvas.Children.Add(p);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    private static Border Tile(Control glyph, double size, string bg = Subtle) =>
        new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * Sizes.TileCorner), Child = glyph, VerticalAlignment = VerticalAlignment.Center }
            .Themed(Border.BackgroundProperty, bg);

    public static Border IconTile(string icon, double size = Sizes.IconTile) => Tile(Icon(icon, size * Sizes.TileGlyph).Themed(TextElement.ForegroundProperty, Accent), size, Selected);

    /// <summary>App logo, or its category icon when the app has no logo.</summary>
    public static Border AppLogo(string? logo, string category, double size = Sizes.LogoL) => LogoOr(logo, Icons.ForCategory(category), size);

    /// <summary>The logo with this key in a tile, or the icon named <paramref name="icon"/> when there is none.</summary>
    public static Border LogoOr(string? logo, string icon, double size = Sizes.LogoL) =>
        Tile(Glyph(Icons.Logo(logo) ?? Icons.Get(icon), size * Sizes.LogoGlyph).Themed(TextElement.ForegroundProperty, Text), size);

    /// <summary>Logo + name, vertically centered; used as the first cell of app rows.</summary>
    /// <summary>Logo and name. The name wraps inside its column (a horizontal StackPanel gave it unlimited width, so it ran into the next column).</summary>
    public static Control AppName(string? logo, string category, string name, double size = Sizes.LogoM) =>
        Beside(AppLogo(logo, category, size), Txt(name, TextSize.Body, true).With(t => t.VerticalAlignment = VerticalAlignment.Center));

    /// <summary>Icon with text beside it that wraps in the space left. Use instead of <see cref="Row"/> when the text may not fit: a Row gives it unlimited width, so it runs past or is cut off.</summary>
    public static Control Beside(Control icon, Control text)
    {
        icon.Margin = new Thickness(0, 0, Space.M, 0); icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);
        return new DockPanel { Children = { icon, text } };
    }

    /// <summary>Level dots are shown only where the design asks for them (dashboard bars, legend, Levels help).</summary>
    public static bool LevelDots { get; set; }

    public static readonly Cursor Hand = new(StandardCursorType.Hand);
}
