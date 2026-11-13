using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Tests;

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
        double before = Contrast.Ratio(text, accent);
        foreach (var amount in new[] { .15, .28 })
            Assert.True(Contrast.Ratio(text, Contrast.AwayFrom(accent, text, amount)) > before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Status_colors_read_on_both_backgrounds(bool dark)
    {
        var s = AiStackprint.Ui.Theme.Status(dark);
        foreach (var c in new[] { s.Error, s.Warning, s.Success })
            foreach (var bg in new[] { s.Background, s.Main })
                Assert.True(Contrast.Ratio(c.ToUInt32(), bg.ToUInt32()) >= 4.5, $"{c} on {bg}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Chart_colors_show_on_the_card(bool dark)
    {
        var card = AiStackprint.Ui.Theme.Status(dark).Background.ToUInt32();
        var marks = AiStackprint.Ui.Theme.Series(dark).Concat(Enumerable.Range(0, 4).Select(i => AiStackprint.Ui.Theme.LevelDefault(dark, i)));
        foreach (var c in marks)
            Assert.True(Contrast.Ratio(c.ToUInt32(), card) >= 3, $"{c} on the card");   // WCAG 1.4.11: chart marks need 3:1
    }
}
