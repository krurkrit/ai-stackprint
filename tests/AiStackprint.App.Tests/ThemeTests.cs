// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Theme colors meet WCAG contrast (needs the app: Theme uses Avalonia colors).</summary>
public class ThemeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Status_colors_read_on_both_backgrounds(bool dark)
    {
        var (error, warning, success, background, main) = Theme.Status(dark);
        foreach (var c in new[] { error, warning, success })
        {
            foreach (var bg in new[] { background, main })
            {
                Assert.True(Contrast.Ratio(c.ToUInt32(), bg.ToUInt32()) >= 4.5, $"{c} on {bg}");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Chart_colors_show_on_the_card(bool dark)
    {
        var card = Theme.Status(dark).Background.ToUInt32();
        var marks = Theme.Series(dark).Concat(Enumerable.Range(0, 4).Select(i => Theme.LevelDefault(dark, i)));
        foreach (var c in marks)
        {
            Assert.True(Contrast.Ratio(c.ToUInt32(), card) >= 3, $"{c} on the card");   // WCAG 1.4.11: chart marks need 3:1
        }
    }
}
