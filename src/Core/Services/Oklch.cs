// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Services;

/// <summary>
/// OKLCH (lightness, chroma, hue: equal steps look equal) for colors given as 0xAARRGGBB (the chart palette in Theme is kept in step with it).
/// </summary>
public static class Oklch
{
    /// <summary>Lightness (0 to 1), chroma and hue (degrees) of an sRGB color.</summary>
    public static (double L, double C, double H) From(uint argb)
    {
        double Lin(int shift)
        {
            var v = ((argb >> shift) & 0xFF) / 255.0;
            return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
        }

        double r = Lin(16), g = Lin(8), b = Lin(0);
        var l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
        var m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
        var s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
        var lightness = .2104542553 * l + .7936177850 * m - .0040720468 * s;
        var a = 1.9779984951 * l - 2.4285922050 * m + .4505937099 * s;
        var bb = .0259040371 * l + .7827717662 * m - .8086757660 * s;
        return (lightness, Math.Sqrt(a * a + bb * bb), (Math.Atan2(bb, a) * 180 / Math.PI + 360) % 360);
    }

    /// <summary>The opaque sRGB color at <paramref name="l"/>, <paramref name="c"/>, <paramref name="h"/>, or null when it is outside sRGB.</summary>
    public static uint? To(double l, double c, double h)
    {
        var a = c * Math.Cos(h * Math.PI / 180);
        var b = c * Math.Sin(h * Math.PI / 180);
        var lc = Math.Pow(l + .3963377774 * a + .2158037573 * b, 3);
        var mc = Math.Pow(l - .1055613458 * a - .0638541728 * b, 3);
        var sc = Math.Pow(l - .0894841775 * a - 1.2914855480 * b, 3);
        double[] rgb =
        [
            4.0767416621 * lc - 3.3077115913 * mc + .2309699292 * sc,
            -1.2684380046 * lc + 2.6097574011 * mc - .3413193965 * sc,
            -.0041960863 * lc - .7034186147 * mc + 1.7076147010 * sc
        ];
        if (rgb.Any(v => v < -1e-4 || v > 1 + 1e-4))
        {
            return null;
        }

        static uint Byte(double v)
        {
            v = Math.Clamp(v, 0, 1);
            return (uint)Math.Round((v <= .0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - .055) * 255);
        }

        return 0xFF000000 | (Byte(rgb[0]) << 16) | (Byte(rgb[1]) << 8) | Byte(rgb[2]);
    }

    /// <summary>The color at <paramref name="l"/> and <paramref name="h"/> with as much chroma as sRGB allows, up to <paramref name="c"/>.</summary>
    public static uint Fit(double l, double c, double h)
    {
        for (; c > 0; c -= .002)
        {
            if (To(l, c, h) is { } x)
            {
                return x;
            }
        }

        return To(l, 0, h) ?? 0xFF808080;
    }
}
