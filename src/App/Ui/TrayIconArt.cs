// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace AiStackprint.Ui;

/// <summary>
/// Pictures the tray draws: the ring of the most-used limit (accent stroke over a track of the text color at 12 %), drawn at 16, 20, 24 and 32 px
/// for 100–200 % scaling, the app icon when there is no reading, and the menu's icons.
/// </summary>
internal static class TrayIconArt
{
    private static readonly int[] _sizes = [16, 20, 24, 32];
    private const double TrackAlpha = 0.12, RingStroke = 0.19, MenuGlyph = 16;   // the ring's stroke as a share of its size (3 in 16)

    public static WindowIcon App()
    {
        using var s = typeof(TrayIconArt).Assembly.GetManifestResourceStream("app-icon.ico")!;
        return new WindowIcon(s);
    }

    /// <summary>The ring for <paramref name="percent"/> (0–100) in the colors of the light or dark theme.</summary>
    public static WindowIcon Ring(double percent, bool dark)
    {
        var (accent, text) = (Color(W.Accent, dark), Color(W.Text, dark));
        if (!OperatingSystem.IsWindows())
        {
            return new WindowIcon(Draw(_sizes[^1], percent, accent, text));
        }

        // An .ico with one PNG per size, so Windows picks the sharp one for the taskbar's scale
        var pngs = _sizes.Select(px =>
        {
            using var b = Draw(px, percent, accent, text);
            using var m = new MemoryStream();
            b.Save(m, new PngBitmapEncoderOptions());
            return m.ToArray();
        }).ToList();
        var ico = new MemoryStream();
        using (var w = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)pngs.Count);
            var offset = 6 + 16 * pngs.Count;
            for (var i = 0; i < pngs.Count; i++)
            {
                w.Write((byte)_sizes[i]);
                w.Write((byte)_sizes[i]);
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write(pngs[i].Length);
                w.Write(offset);
                offset += pngs[i].Length;
            }

            pngs.ForEach(w.Write);
        }

        ico.Position = 0;
        return new WindowIcon(ico);
    }

    public static RenderTargetBitmap Draw(int px, double percent, Color accent, Color text)
    {
        var b = new RenderTargetBitmap(new PixelSize(px, px));
        using var c = b.CreateDrawingContext();
        var stroke = px * RingStroke;
        var r = (px - stroke) / 2;
        var center = new Point(px / 2.0, px / 2.0);
        c.DrawEllipse(null, new Pen(new SolidColorBrush(text, TrackAlpha), stroke), center, r, r);
        var p = Math.Clamp(percent, 0, 100) / 100;
        if (p >= 0.999)
        {
            c.DrawEllipse(null, new Pen(new SolidColorBrush(accent), stroke), center, r, r);
        }
        else if (p > 0)
        {
            var g = new StreamGeometry();
            using (var x = g.Open())
            {
                var a = p * 2 * Math.PI;
                x.BeginFigure(new Point(center.X, center.Y - r), false);
                x.ArcTo(new Point(center.X + r * Math.Sin(a), center.Y - r * Math.Cos(a)), new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise);
                x.EndFigure(false);
            }

            c.DrawGeometry(null, new Pen(new SolidColorBrush(accent), stroke, lineCap: PenLineCap.Round), g);
        }

        return b;
    }

    /// <summary>A menu icon from icons.json in the muted color of the theme the menu is drawn in.</summary>
    public static Bitmap Glyph(string icon, bool dark, double scale = 2)
    {
        var px = (int)(MenuGlyph * scale);
        var b = new RenderTargetBitmap(new PixelSize(px, px), new Vector(96 * scale, 96 * scale));
        using var c = b.CreateDrawingContext();
        var k = MenuGlyph / Sizes.IconBox;
        using (c.PushTransform(Matrix.CreateScale(k, k)))
        {
            foreach (var s in Icons.Get(icon))
            {
                c.DrawGeometry(new SolidColorBrush(Color(W.Muted, dark), s.Opacity), null, s.Data);
            }
        }

        return b;
    }

    /// <summary>A theme color of the light or dark theme (with the user's accent), whatever theme the app shows.</summary>
    public static Color Color(string key, bool dark)
    {
        var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        return Application.Current!.TryGetResource(key, variant, out var v) ? v switch
        {
            Color c => c,
            ISolidColorBrush s => s.Color,
            _ => Colors.Gray
        } : Colors.Gray;
    }

    /// <summary>
    /// Whether the taskbar (Windows) is dark, which may differ from the app's theme; elsewhere the app's theme.
    /// </summary>
    public static bool TaskbarDark()
    {
        if (OperatingSystem.IsWindows())
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (k?.GetValue("SystemUsesLightTheme") is int light)
            {
                return light == 0;
            }
        }

        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    }
}
