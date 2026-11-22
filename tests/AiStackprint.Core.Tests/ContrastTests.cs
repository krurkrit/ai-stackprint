// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class ContrastTests
{
    // Reference contrast values
    [Theory]
    [InlineData(0xFF34383Cu, 0xFFF8F9FAu, 11.21)]
    [InlineData(0xFF1450A0u, 0xFFF8F9FAu, 7.42)]
    [InlineData(0xFFF8F9FAu, 0xFF18191Bu, 16.69)]
    [InlineData(0xFF18191Bu, 0xFF74A8F4u, 7.25)]
    public void Matches_the_reference_values(uint fg, uint bg, double expected) =>
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

    [Theory]
    [InlineData(0xFFFFE066u, 0xFF141517u)]   // pale yellow with dark text: already passes, unchanged
    [InlineData(0xFF6FA8FFu, 0xFFFFFFFFu)]   // light blue with white text: goes darker
    [InlineData(0xFF808080u, 0xFFFFFFFFu)]   // mid gray with white text
    [InlineData(0xFF808080u, 0xFF000000u)]   // mid gray with black text
    public void AtLeast_moves_a_color_just_far_enough_from_its_text(uint color, uint text)
    {
        var c = Contrast.AtLeast(color, text, Contrast.Minimum);
        Assert.True(Contrast.Ratio(text, c) >= Contrast.Minimum);
        var step = Enumerable.Range(0, 101).First(i => Contrast.AwayFrom(color, text, i / 100.0) == c);
        Assert.True(step == 0 || Contrast.Ratio(text, Contrast.AwayFrom(color, text, (step - 1) / 100.0)) < Contrast.Minimum);   // the least that passes
    }

    [Fact]
    public void AtLeast_keeps_a_color_that_already_passes() =>
        Assert.Equal(0xFF1450A0u, Contrast.AtLeast(0xFF1450A0u, 0xFFFFFFFFu, Contrast.Minimum));
}
