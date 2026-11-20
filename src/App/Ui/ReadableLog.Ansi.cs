// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using AiStackprint.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class ReadableLog
{
    /// <summary>Shell output with its colors. Palette follows the theme at the time the step is opened.</summary>
    private static SelectableTextBlock AnsiText(List<TextDecode.AnsiRun> runs)
    {
        var tb = new SelectableTextBlock
        {
            FontFamily = Theme.Mono,
            FontSize = Theme.MonoSize,
            TextWrapping = TextWrapping.Wrap
        }.Themed(TextBlock.ForegroundProperty, Text);
        var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        tb.Inlines ??= [];
        foreach (var r in runs)
        {
            var run = new Run(r.Text);
            if (r.Color is { } c)
            {
                run.Foreground = new ImmutableSolidColorBrush(AnsiColor(c, dark));
            }

            if (r.Bold)
            {
                run.FontWeight = FontWeight.Bold;
            }

            tb.Inlines.Add(run);
        }
        return tb;
    }

    private static readonly string[] _ansiDark =
        ["#6E7681", "#F47067", "#57AB5A", "#C69026", "#539BF5", "#B083F0", "#39C5CF", "#ADBAC7", "#909DAB", "#FF938A", "#6BC46D", "#DAAA3F", "#6CB6FF", "#DCBDFB", "#56D4DD", "#CDD9E5"];
    private static readonly string[] _ansiLight =
        ["#24292F", "#CF222E", "#116329", "#7D4E00", "#0550AE", "#8250DF", "#1B7C83", "#6E7781", "#57606A", "#A40E26", "#1A7F37", "#633C01", "#0969DA", "#6639BA", "#3192AA", "#8C959F"];

    private static Color AnsiColor(int c, bool dark) =>
        c >= 0x1000000 ? Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c) : Color.Parse((dark ? _ansiDark : _ansiLight)[Math.Clamp(c, 0, 15)]);

    /// <summary>Builds the expander's content the first time it opens, so hundreds of steps stay fast.</summary>
    private static Expander Lazy(Expander x, Func<Control> content)
    {
        x.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty && x.IsExpanded && x.Content == null) { x.Content = content(); } };
        return x;
    }

    private static string FirstLine(string s)
    {
        var l = s.TrimStart().Split('\n')[0];
        return l.Length > 120 ? l[..120] + "…" : l;
    }

    [GeneratedRegex(@"```([\w+#.-]*)[^\n]*\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex FenceBlock();
}
