// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AiStackprint.Ui;

/// <summary>Small widget factory. Colors are resource keys defined in <see cref="Theme"/>, so light/dark work automatically.</summary>
internal static partial class W
{
    // Resource keys (values in Theme)
    internal const string Background = "Ap.Background", Text = "Ap.Text", Muted = "Ap.Muted", Surface = "Ap.Surface", SideKey = "Ap.Side", MainKey = "Ap.Main", TopKey = "Ap.Top";
    internal const string Subtle = "Ap.Hover", Selected = "Ap.Selected", BorderKey = "Ap.Divider";
    internal const string Accent = "Ap.Accent", OnAccent = "Ap.OnAccent", Link = "Ap.Link";
    internal const string ErrorKey = "Ap.Error", ErrorFill = "Ap.ErrorFill", ErrorHover = "Ap.ErrorHover", ErrorPressed = "Ap.ErrorPressed", OnError = "Ap.OnError", WarningKey = "Ap.Warning", SuccessKey = "Ap.Success";
    internal const string ChartIn = "Ap.ChartIn", ChartOut = "Ap.ChartOut", ChartWrite = "Ap.ChartWrite", ChartRead = "Ap.ChartRead";
    internal const string SuccessText = "Ap.SuccessText", SelectionKey = "Ap.Selection", HighlightKey = "Ap.Highlight", HighlightRingKey = "Ap.HighlightRing", ScrimKey = "Ap.Scrim";

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
    /// <summary>Emphasis and Side menu may have their own family; a size of their own scales the text (their size over Body's: 1 when they follow it).</summary>
    public static FontFamily? EmphasisFont { get; set; }
    public static FontFamily? MenuFont { get; set; }
    public static double EmphasisScale { get; set; } = 1;
    public static double HeadingScale { get; set; } = 1;
    public static double MenuScale { get; set; } = 1;
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
    {
        c.Bind(p, c.GetResourceObservable(key));
        return c;
    }

    public static T With<T>(this T t, Action<T> a)
    {
        a(t);
        return t;
    }

    /// <summary>Detaches a long-lived control from its old page so a rebuilt page can host it again.</summary>
    public static T Free<T>(this T c) where T : Control
    {
        switch (c.Parent)
        {
            case Panel p:
                p.Children.Remove(c);
                break;
            case ContentControl cc:
                cc.Content = null;
                break;
            case Decorator d:
                d.Child = null;
                break;
        }
        return c;
    }

    /// <summary>Text at a 14-px-based size; bold = the Emphasis weight. It can be selected by dragging and copied, except inside a button or a clickable row (<see cref="CopyableText"/>).</summary>
    public static TextBlock Txt(string s, double size = TextSize.Body, bool bold = false, string key = Text) =>
        new CopyableText
        {
            Text = s,
            FontSize = Size(bold ? size * EmphasisScale : size),
            FontWeight = bold ? Emphasis : Body,
            TextWrapping = TextWrapping.Wrap
        }.With(t =>
        {
            if (bold && EmphasisFont != null) { t.FontFamily = EmphasisFont; }
            NumberStyle(t, s);
        })
            .Themed(TextBlock.ForegroundProperty, key);

    /// <summary>Like <see cref="Txt"/> but the text can be selected and copied by dragging. Only for text that is not part of a click target.</summary>
    public static SelectableTextBlock Sel(string s, double size = TextSize.Body, bool bold = false, string key = Text) =>
        new SelectableTextBlock
        {
            Text = s,
            FontSize = Size(bold ? size * HeadingScale : size),
            FontWeight = bold ? Heading : Body,
            TextWrapping = TextWrapping.Wrap
        }
            .With(t => { if (bold && HeadingFont != null) { t.FontFamily = HeadingFont; } })
            .Themed(TextBlock.ForegroundProperty, key);

    /// <summary>Card titles, names in headers, big numbers: Heading weight.</summary>
    public static TextBlock Title(string s, double size = TextSize.Subtitle) =>
        new CopyableText
        {
            Text = s,
            FontSize = Size(size * HeadingScale),
            FontWeight = Heading,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        }
            .With(t =>
            {
                if (HeadingFont != null) { t.FontFamily = HeadingFont; }
                NumberStyle(t, s);
            });

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
        if (go != null)
        {
            p.Cursor = Hand;
            p.PointerReleased += (_, _) => GoAction?.Invoke(go);
        }
        var sep = Title("›", TextSize.Page).Themed(TextBlock.ForegroundProperty, Muted);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.L,
            Children = { p, sep, Title(name, TextSize.Page) }
        };
    }
}
