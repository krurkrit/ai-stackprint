// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class ReadableLog
{
    /// <summary>Text the app injected around the message (instructions, environment, reminders, marker blocks): its own collapsed area.</summary>
    private static Control Injected(ContextBlocks.Part part) => Fold($"Injected · {part.Label} · ", part.Text, null, new Thickness(0), markdown: true);

    internal static Control Code(string text, string ext) =>
        new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = new CodeView(ext, lineNumbers: false, compact: true) { Text = text.TrimEnd('\n') } };

    private static readonly Regex _fence = new(@"```([\w+#.-]*)[^\n]*\n(.*?)```", RegexOptions.Singleline);

    /// <summary>Splits markdown into prose and fenced code blocks; the fence language picks the grammar.</summary>
    internal static IEnumerable<(string Text, string? Lang)> Fences(string md)
    {
        var at = 0;
        foreach (Match m in _fence.Matches(md))
        {
            if (m.Index > at && md[at..m.Index].Trim() != "")
            {
                yield return (md[at..m.Index], null);
            }

            yield return (m.Groups[2].Value, Ext(m.Groups[1].Value));
            at = m.Index + m.Length;
        }
        if (at < md.Length && md[at..].Trim() != "")
        {
            yield return (md[at..], null);
        }
    }

    private static string Ext(string fence) => fence.ToLowerInvariant() switch
    {
        "" or "text" or "txt" or "plain" => ".txt",
        "csharp" or "cs" or "c#" => ".cs",
        "js" or "javascript" => ".js",
        "ts" or "typescript" => ".ts",
        "bash" or "sh" or "shell" or "zsh" or "console" => ".sh",
        "powershell" or "ps1" or "pwsh" => ".ps1",
        "python" or "py" => ".py",
        "yaml" or "yml" => ".yaml",
        "toml" => ".ini",
        "markdown" or "md" => ".md",
        "rust" or "rs" => ".rs",
        "golang" or "go" => ".go",
        var x => "." + x
    };

    private static Control ToolCall(LogEntry e)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8 };
        line.Children.Add(Icon("wrench", 14).Themed(TextElement.ForegroundProperty, Muted));
        line.Children.Add(L(e.Tool ?? "tool", true).With(t => { Grid.SetColumn(t, 1); t.VerticalAlignment = VerticalAlignment.Center; }));
        var args = new TextBlock { Text = TextDecode.Strip(e.Text.Split('\n')[0]), FontFamily = Theme.Mono, FontSize = Theme.MonoSize, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, Muted);
        Grid.SetColumn(args, 2); line.Children.Add(args);
        // No arrow when opening would show the same one line the header already shows
        if (string.IsNullOrEmpty(e.Detail) || SameAsHeader(e.Detail, args.Text ?? ""))
        {
            return line.With(l => l.Margin = new Thickness(12, 2));
        }
        // Full input (command, JSON arguments, patch) highlighted when opened
        return Lazy(new Expander { Header = line, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(12, 0, 0, 0) }, () => Code(e.Detail, e.Lang ?? ".txt"));
    }

    /// <summary>Collapsed by default; long tool output and thinking stay out of the way. Highlighted when opened.</summary>
    private static Control Folded(string label, string text, string? lang, bool markdown = false) => Fold(label + " · ", text, lang, new Thickness(12, 0, 0, 0), markdown);

    /// <summary>An expander whose header is the label and first line; a plain line when the text is that one line (opening would add nothing).</summary>
    private static Control Fold(string label, string text, string? lang, Thickness margin, bool markdown = false)
    {
        var first = FirstLine(TextDecode.Clean(text.Length > 400 ? text[..400] : text));
        var header = L(label + first, false, Muted).With(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; });
        if (SameAsHeader(text, first))
        {
            return header.With(h => h.Margin = new Thickness(margin.Left + 12, 6, 0, 6));
        }

        return Lazy(new Expander { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = margin }, () => markdown ? Rendered(TextDecode.Strip(text)) : StepBody(text, lang));
    }

    /// <summary>The opened text would equal the header line: one short line that decoding does not change.</summary>
    private static bool SameAsHeader(string text, string header)
    {
        var t = text.Trim();
        return t.Length <= 120 && !t.Contains('\n') && t == header.Trim() && !TextDecode.Prepare(t, null).Changed;
    }

    /// <summary>Opened content: decoded text (JSON pretty, escapes, shell colors, image data shortened) with a toggle back to the original when it was changed.</summary>
    private static Control StepBody(string original, string? lang)
    {
        if (lang is ".md" or ".markdown")
        {
            return Rendered(original);
        }

        var p = TextDecode.Prepare(original, lang);
        Control Show(bool fmt) => !fmt ? Plain(original) : p.Ansi != null ? AnsiText(p.Ansi) : p.Lang != null ? Code(p.Text, p.Lang) : Plain(p.Text);
        var view = new ContentControl { Content = Show(true) };
        if (!p.Changed)
        {
            return view;
        }

        var formatted = true;
        var toggle = new Button { Content = "Show original", Padding = new Thickness(8, 2), MinHeight = 0, FontSize = Size(12), HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Click += (_, _) =>
        {
            formatted = !formatted;
            view.Content = Show(formatted);
            toggle.Content = formatted ? "Show original" : "Show formatted";
        };
        return new StackPanel { Spacing = 4, Children = { toggle, view } };
    }

    /// <summary>Markdown in the body font; a Source toggle shows the text as written.</summary>
    private static Control Rendered(string md)
    {
        var view = new ContentControl { Content = MarkdownView.Document(md) };
        var source = false;
        var toggle = new Button { Content = "Source", Padding = new Thickness(8, 2), MinHeight = 0, FontSize = Size(12), HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Click += (_, _) =>
        {
            source = !source;
            view.Content = source ? Code(md, ".md") : MarkdownView.Document(md);
            toggle.Content = source ? "Rendered" : "Source";
        };
        return new StackPanel { Spacing = 4, Children = { toggle, view } };
    }

    private static Control Plain(string text) =>
        new SelectableTextBlock { Text = text, FontFamily = Theme.Mono, FontSize = Theme.MonoSize, TextWrapping = TextWrapping.Wrap }.Themed(TextBlock.ForegroundProperty, Text);

    /// <summary>Shell output with its colors. Palette follows the theme at the time the step is opened.</summary>
    private static Control AnsiText(List<TextDecode.AnsiRun> runs)
    {
        var tb = new SelectableTextBlock { FontFamily = Theme.Mono, FontSize = Theme.MonoSize, TextWrapping = TextWrapping.Wrap }.Themed(TextBlock.ForegroundProperty, Text);
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

    private static string FirstLine(string s) { var l = s.TrimStart().Split('\n')[0]; return l.Length > 120 ? l[..120] + "…" : l; }
}
