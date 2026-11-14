// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Appearance › Colors: accent, hover and selection for Light and Dark. Hex box + Snowy Owl swatches, reset, contrast hint.</summary>
internal static class ColorSettings
{
    private const double SwatchesWidth = 240;   // eight swatches per row
    public static Control Build(Settings st, Action<Action> change)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Widths.Label},*,*"), RowSpacing = Space.L, ColumnSpacing = Space.XL };
        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        g.Children.Add(Txt("Light theme", TextSize.Small, true).With(t => Grid.SetColumn(t, 1)));
        g.Children.Add(Txt("Dark theme", TextSize.Small, true).With(t => Grid.SetColumn(t, 2)));
        var rows = new (string Label, string Hint, Func<ThemeColors, string?> Get, Action<ThemeColors, string?> Set, int Which)[]
        {
            ("Accent", "Buttons, links, selected menu item", c => c.Accent, (c, v) => c.Accent = v, 0),
            ("Hover", "Rows and buttons under the pointer", c => c.Hover, (c, v) => c.Hover = v, 1),
            ("Selection", "Selected text in logs, editors and text", c => c.Selection, (c, v) => c.Selection = v, 2),
        }.Concat(Enumerable.Range(0, Theme.LevelNames.Length).Select(i => ((string Label, string Hint, Func<ThemeColors, string?> Get, Action<ThemeColors, string?> Set, int Which))
            ($"{Theme.LevelNames[i]} level", i == 0 ? "Dashboard chart only" : "", c => c.Levels?.ElementAtOrDefault(i), (c, v) => { c.Levels ??= new string?[Theme.LevelNames.Length]; c.Levels[i] = v; }, 3 + i))).ToArray();
        foreach (var r in rows)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var row = g.RowDefinitions.Count - 1;
            var label = new StackPanel { Children = { Txt(r.Label, TextSize.Body, false, Muted), Txt(r.Hint, TextSize.Tiny, false, Muted) } };
            Grid.SetRow(label, row); g.Children.Add(label);
            for (var t = 0; t < 2; t++)
            {
                var dark = t == 1;
                var colors = dark ? st.Dark : st.Light;
                var cell = Editor(r.Get(colors), Default(dark, r.Which), dark, r.Which, v => change(() => r.Set(colors, v)));
                Grid.SetRow(cell, row); Grid.SetColumn(cell, t + 1); g.Children.Add(cell);
            }
        }
        var reset = Btn("Reset level colors", () => change(() => { st.Light.Levels = null; st.Dark.Levels = null; }), false, "arrow_reset").With(b => b.HorizontalAlignment = HorizontalAlignment.Left);
        return new StackPanel { Spacing = Space.L, Children = { g, reset } };
    }

    private static Color Default(bool dark, int which)
    {
        if (which >= 3)
        {
            return Theme.LevelDefault(dark, which - 3, Theme.Defaults(dark).Accent);
        }

        var d = Theme.Defaults(dark); return which switch { 0 => d.Accent, 1 => d.Hover, _ => d.Selection };
    }

    private static string Hex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>[swatch: opens the picker][#RRGGBB][opacity %][↺] plus a contrast note. Opacity is its own 0–100% setting; the stored value is #RRGGBB or #AARRGGBB.</summary>
    private static StackPanel Editor(string? value, Color fallback, bool dark, int which, Action<string?> set)
    {
        var current = Theme.ParseHex(value) ?? fallback;
        var box = new TextBox { Text = value == null ? "" : Rgb(current), PlaceholderText = Rgb(fallback), Width = Widths.FieldS, FontFamily = Theme.Mono };
        var opacity = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 5, FormatString = "0", Width = Widths.FieldS, Value = (decimal)Math.Round(current.A / 255.0 * 100) };
        ToolTip.SetTip(opacity, "Opacity, 0 to 100 %");
        var defaultPct = (int)Math.Round(fallback.A / 255.0 * 100);

        void Commit()
        {
            var t = (box.Text ?? "").Trim();
            var rgb = t == "" ? null : Theme.ParseHex(t.StartsWith('#') ? t : "#" + t);
            if (t != "" && rgb == null) { box.Text = value == null ? "" : Rgb(current); return; }   // invalid hex: put the old value back
            var pct = (int)Math.Clamp(opacity.Value ?? defaultPct, 0, 100);
            if (rgb == null && pct == defaultPct) { if (value != null) { set(null); } return; }
            var c = rgb ?? fallback;
            var next = Hex(Color.FromArgb((byte)Math.Round(pct * 2.55), c.R, c.G, c.B));
            if (next != value)
            {
                set(next);
            }
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { Commit(); } };
        opacity.ValueChanged += (_, e) => { if (e.NewValue is { } v && (int)v != (int)Math.Round(current.A / 255.0 * 100)) { Commit(); } };

        var view = new ColorView { Color = Color.FromRgb(current.R, current.G, current.B), IsAlphaVisible = false, IsAlphaEnabled = false, IsColorSpectrumVisible = true, IsComponentTextInputVisible = false, IsHexInputVisible = true };
        var swatches = new WrapPanel { Width = SwatchesWidth, Margin = new Thickness(0, Space.M, 0, 0) };
        foreach (var (name, hex) in Theme.Swatches)
        {
            var sc = Color.Parse(hex);
            var sw = new Button { Width = Sizes.Swatch, Height = Sizes.Swatch, Margin = new Thickness(0, 0, Space.S, Space.S), Padding = new Thickness(0), Background = new SolidColorBrush(sc), CornerRadius = new CornerRadius(Radius.M) };
            ToolTip.SetTip(sw, $"{name} {Rgb(sc)}");
            sw.Click += (_, _) => view.Color = sc;
            swatches.Children.Add(sw);
        }
        var flyout = new Flyout { Content = new StackPanel { Children = { view, swatches } } };
        flyout.Closed += (_, _) => { box.Text = Rgb(view.Color); Commit(); };   // committed on close: each change rebuilds the page, which would close the picker
        var preview = new Button { Width = Sizes.CompactControl, Height = Sizes.CompactControl, Padding = new Thickness(0), CornerRadius = new CornerRadius(Radius.M), BorderThickness = new Thickness(Stroke.Thin), Background = new SolidColorBrush(current), Flyout = flyout }
            .Themed(Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, BorderKey);
        ToolTip.SetTip(preview, "Pick a color");
        var reset = IconBtn("arrow_reset", "Reset to Snowy Owl default", () => set(null));
        reset.IsEnabled = value != null;

        var sp = new StackPanel { Spacing = Space.XS, Children = { Row(preview, box, opacity, Txt("%", TextSize.Small, false, Muted), reset) } };
        if (Note(current, dark, which) is { } note)
        {
            sp.Children.Add(Txt(note, TextSize.Tiny, false, Muted));
        }

        return sp;
    }

    private static string Rgb(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Contrast against what the color is used with; a hint below Snowy Owl's 7.1:1, never a block.</summary>
    private static string? Note(Color c, bool dark, int which)
    {
        var d = Theme.Defaults(dark);
        var ratio = which switch
        {
            0 => Math.Max(ContrastRatio(Theme.AccentTexts.Light, c), ContrastRatio(Theme.AccentTexts.Dark, c)),   // button text on the accent (the app picks the better one)
            2 => ContrastRatio(d.Text, Color.FromUInt32(Services.Contrast.Blend(c.ToUInt32(), d.Background.ToUInt32()))),   // text on top of the selection
            _ => 99
        };
        return ratio < Services.Contrast.Minimum ? $"Contrast {ratio:0.0}:1 — below Snowy Owl's 7.1:1" : null;
    }
}
