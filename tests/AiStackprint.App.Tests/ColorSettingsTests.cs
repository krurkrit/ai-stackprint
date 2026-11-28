// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Settings › Appearance › Colors: every color shows its value as real text, so it can be selected and copied.</summary>
public sealed class ColorSettingsTests
{
    [AvaloniaFact]
    public void A_default_color_shows_its_hex_as_text_not_only_as_a_placeholder()
    {
        var saved = 0;
        var window = Ui.Show(ColorSettings.Build(SettingsStore.Defaults(), change => { change(); saved++; }));
        var boxes = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible && t.FindAncestorOfType<NumericUpDown>() == null).ToList();

        Assert.NotEmpty(boxes);
        Assert.All(boxes, b => Assert.Matches("^#[0-9A-F]{6}$", b.Text ?? ""));
        Assert.Equal(0, saved);   // showing the default writes nothing
        Assert.All(boxes, b => Assert.Equal("Pick a color", ToolTip.GetTip(Assert.IsType<Button>(b.InnerLeftContent))));   // [■ #RRGGBB]: the swatch is inside the box
    }

    [AvaloniaFact]
    public void A_swatch_keeps_its_color_under_the_pointer()
    {
        var window = Ui.Show(ColorSettings.Build(SettingsStore.Defaults(), change => change()));
        var swatch = window.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "Pick a color");
        var color = Assert.IsType<Avalonia.Media.SolidColorBrush>(swatch.Background).Color;
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var at = swatch.TranslatePoint(new Avalonia.Point(swatch.Bounds.Width / 2, swatch.Bounds.Height / 2), window) ?? default;
        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, at);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(swatch.IsPointerOver);
        var fill = swatch.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        Assert.Equal(color, Assert.IsType<Avalonia.Media.SolidColorBrush>(fill.Background).Color);   // not the hover gray
    }

    [AvaloniaFact]
    public void Leaving_a_default_color_unchanged_saves_nothing()
    {
        var saved = 0;
        var window = Ui.Show(ColorSettings.Build(SettingsStore.Defaults(), change => { change(); saved++; }));
        var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.FindAncestorOfType<NumericUpDown>() == null);

        box.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Enter
        });

        Assert.Equal(0, saved);
    }

    [AvaloniaFact]
    public void Opacity_shows_as_a_percent_and_reads_typed_text_with_or_without_the_percent()
    {
        var window = Ui.Show(ColorSettings.Build(SettingsStore.Defaults(), change => change()));
        var opacity = window.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.IsEffectivelyVisible);
        var text = opacity.GetVisualDescendants().OfType<TextBox>().First();

        Assert.Matches("^[0-9]{1,3}%$", text.Text ?? "");   // not empty
        Assert.Equal(opacity.Value + "%", text.Text);
        Assert.Equal("60%", opacity.TextConverter!.ConvertBack(60m, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(40m, opacity.TextConverter.Convert("40%", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(40m, opacity.TextConverter.Convert(" 40 ", typeof(decimal?), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}
