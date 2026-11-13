// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class ContrastTests
{
    // Values from snowy-owl/docs/ACCESSIBILITY.md
    [Theory]
    [InlineData(0xFF34383Cu, 0xFFF8F9FAu, 11.21)]
    [InlineData(0xFF1450A0u, 0xFFF8F9FAu, 7.42)]
    [InlineData(0xFFF8F9FAu, 0xFF18191Bu, 16.69)]
    [InlineData(0xFF18191Bu, 0xFF74A8F4u, 7.25)]
    public void Matches_snowy_owl_report(uint fg, uint bg, double expected) =>
        Assert.Equal(expected, Math.Round(Contrast.Ratio(fg, bg), 2), 2);

    [Fact]
    public void Translucent_colour_is_blended_over_background()
    {
        Assert.Equal(0xFF808080u, Contrast.Blend(0x80FFFFFFu, 0xFF000000u));
        Assert.Equal(1.0, Contrast.Ratio(0x00FFFFFFu, 0xFF123456u), 3);   // fully transparent = background
    }

    [Theory]
    [InlineData(0xFF1450A0u, 0xFFF8F9FAu)]   // light accent, white text: hover goes darker
    [InlineData(0xFFFD7E14u, 0xFF141517u)]   // dark accent (orange), dark text: hover goes lighter
    public void Hover_shade_keeps_text_readable(uint accent, uint text)
    {
        var before = Contrast.Ratio(text, accent);
        foreach (var amount in new[] { .15, .28 })
        {
            Assert.True(Contrast.Ratio(text, Contrast.AwayFrom(accent, text, amount)) > before);
        }
    }
}
