// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Media;

namespace AiStackprint.Ui;

internal static partial class W
{
    /// <summary>
    /// A clickable row or chip: under the pointer its background eases to <paramref name="over"/> (default <see cref="Subtle"/>) and back to
    /// <paramref name="rest"/> (default transparent) in <see cref="Duration.Faster"/>. Buttons get the same from Fluent.
    /// </summary>
    public static T Hover<T>(this T b, string? over = null, string? rest = null) where T : Border
    {
        b.Transitions = Motion.Hover();
        b.PointerEntered += (_, _) => b.Themed(Border.BackgroundProperty, over ?? Subtle);
        b.PointerExited += (_, _) =>
        {
            if (rest == null)
            {
                b.Background = Brushes.Transparent;
            }
            else
            {
                b.Themed(Border.BackgroundProperty, rest);
            }
        };
        return b;
    }

    /// <summary>
    /// A color swatch keeps its color and border under the pointer and when pressed (a hover fill would hide the color it shows); the hand cursor says it can be clicked.
    /// Call after setting its background.
    /// </summary>
    public static Button KeepFill(this Button b)
    {
        b.Cursor = Hand;
        var app = Avalonia.Application.Current!;
        foreach (var v in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
        {
            var d = new Avalonia.Controls.ResourceDictionary();
            foreach (var state in new[] { "PointerOver", "Pressed" })
            {
                d["ButtonBackground" + state] = b.Background;
                if (app.TryGetResource(BorderKey, v, out var border))
                {
                    d["ButtonBorderBrush" + state] = border;
                }
            }

            b.Resources.ThemeDictionaries[v] = d;
        }

        return b;
    }
}
