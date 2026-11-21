// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

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

}
