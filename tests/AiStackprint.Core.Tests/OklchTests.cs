// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class OklchTests
{
    [Theory]
    [InlineData(0xFF1450A0u)]
    [InlineData(0xFFFD7E14u)]
    [InlineData(0xFF019D2Au)]
    [InlineData(0xFF808080u)]
    public void A_color_comes_back_the_same_after_a_round_trip(uint argb)
    {
        var (l, c, h) = Oklch.From(argb);
        Assert.Equal(argb, Oklch.To(l, c, h));
    }

    [Fact]
    public void A_color_outside_srgb_is_none_and_fit_lowers_its_chroma_until_it_is_in()
    {
        Assert.Null(Oklch.To(.9, .3, 260));
        var fit = Oklch.Fit(.9, .3, 260);
        Assert.True(Oklch.From(fit).C < .3);
        Assert.InRange(Oklch.From(fit).H, 257, 263);   // the same hue, give or take rounding to 8 bits
    }
}
