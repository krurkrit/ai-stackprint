// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Number and color boxes are as wide as what they show: no clipped digits, no empty space, the same gap at both ends.</summary>
public sealed class InputFitTests
{
    private static Rect In(Visual v, Visual root) => new(v.TranslatePoint(default, root)!.Value, v.Bounds.Size);

    [AvaloniaTheory]
    [InlineData("00", 14)]
    [InlineData("100%", 100)]
    [InlineData("+000", -200)]
    public void A_stepper_fits_its_widest_text_between_the_buttons(string widest, int value)
    {
        var box = W.Stepper(new NumericUpDown { Minimum = -999, Maximum = 999, Value = value }, widest);
        var w = Ui.Show(new StackPanel { Children = { box } });
        var text = box.GetVisualDescendants().OfType<TextPresenter>().First();
        var minus = (Button)box.InnerLeftContent!;
        var plus = (Button)box.InnerRightContent!;
        var room = In(plus, box).Left - In(minus, box).Right;
        Assert.InRange(room, W.TextWidth(widest), W.TextWidth(widest) + 2 * Space.M);   // fits, without much to spare
        Assert.True(In(text, box).Left >= In(minus, box).Right && In(text, box).Right <= In(plus, box).Left);   // the text shown sits between them
        w.Close();
    }

    [AvaloniaFact]
    public void A_color_box_has_the_same_gap_before_the_swatch_as_after_the_code()
    {
        var w = Ui.Show(ColorSettings.Build(SettingsStore.Defaults(), change => change()));
        var box = w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.FindAncestorOfType<NumericUpDown>() == null);
        var swatch = (Button)box.InnerLeftContent!;
        var text = In(box.GetVisualDescendants().OfType<TextPresenter>().First(), box);
        var before = In(swatch, box).Left - Stroke.Thin;   // inside the border
        var after = box.Bounds.Width - Stroke.Thin - text.Right;
        Assert.Equal(Space.S, before);
        Assert.Equal(before, after);
        Assert.Equal(Space.S, text.Left - In(swatch, box).Right);
        Assert.True(text.Width >= W.TextWidth("#AAAAAA", Theme.Mono) - 1);   // all 7 characters show
        w.Close();
    }
}
