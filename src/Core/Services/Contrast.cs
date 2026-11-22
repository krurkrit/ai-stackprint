// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Services;

/// <summary>WCAG 2 contrast for colors given as 0xAARRGGBB. Translucent colors are blended over the background first.</summary>
public static class Contrast
{
    /// <summary>The app's minimum (stricter than WCAG AAA 7:1).</summary>
    public static readonly double Minimum = 7.1;

    public static double Ratio(uint fg, uint bg)
    {
        double a = Luminance(Blend(fg, bg)), b = Luminance(bg | 0xFF000000);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Color <paramref name="top"/> painted over an opaque <paramref name="under"/>.</summary>
    public static uint Blend(uint top, uint under)
    {
        var alpha = (top >> 24) / 255.0;
        uint Mix(int shift) => (uint)Math.Round(((top >> shift) & 0xFF) * alpha + ((under >> shift) & 0xFF) * (1 - alpha));
        return 0xFF000000 | (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
    }

    /// <summary>
    /// <paramref name="color"/> mixed toward black or white, whichever moves it away from <paramref name="text"/>,
    /// so text on the result always reads at least as well as on the original (hover and pressed fills).
    /// </summary>
    public static uint AwayFrom(uint color, uint text, double amount) =>
        Mix(color, Luminance(text | 0xFF000000) > Luminance(color | 0xFF000000) ? 0xFF000000u : 0xFFFFFFFFu, amount);

    /// <summary><paramref name="color"/> mixed <paramref name="amount"/> (0 to 1) of the way toward <paramref name="toward"/> (opaque result).</summary>
    public static uint Mix(uint color, uint toward, double amount)
    {
        var a = (uint)Math.Round(Math.Clamp(amount, 0, 1) * 255);
        return Blend((a << 24) | (toward & 0xFFFFFF), color | 0xFF000000);
    }

    /// <summary>
    /// <paramref name="color"/> moved away from <paramref name="text"/> (toward black or white) by the least that gives text on it at least
    /// <paramref name="min"/>:1, and on each of <paramref name="states"/> made from it (a hover fill, say); unchanged when all already do.
    /// A custom accent stays as close to the chosen color as the minimum allows.
    /// </summary>
    public static uint AtLeast(uint color, uint text, double min, params Func<uint, uint>[] states)
    {
        for (var step = 0; step <= 100; step++)
        {
            var c = AwayFrom(color, text, step / 100.0);
            if (Ratio(text, c) >= min && states.All(s => Ratio(text, s(c)) >= min))
            {
                return c;
            }
        }

        return AwayFrom(color, text, 1);   // black or white: the most there is
    }

    private static double Luminance(uint c)
    {
        double Ch(int shift)
        {
            var v = ((c >> shift) & 0xFF) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(16) + 0.7152 * Ch(8) + 0.0722 * Ch(0);
    }
}
