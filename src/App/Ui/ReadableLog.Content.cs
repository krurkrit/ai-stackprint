// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class ReadableLog
{
    /// <summary>Text the app injected around the message (instructions, environment, reminders, marker blocks): its own collapsed area.</summary>
    private static Control Injected(ContextBlocks.Part part) => Fold($"Injected · {part.Label} · ", part.Text, null, new Thickness(0), markdown: true);

    internal static Control Code(string text, string ext) =>
        new Border
        {
            CornerRadius = new CornerRadius(Radius.M),
            ClipToBounds = true,
            Child = new CodeView(ext, lineNumbers: false, compact: true) { Text = text.TrimEnd('\n') }
        };

    /// <summary>Splits markdown into prose and fenced code blocks; the fence language picks the grammar.</summary>
    internal static IEnumerable<(string Text, string? Lang)> Fences(string md)
    {
        var at = 0;
        foreach (Match m in FenceBlock().Matches(md))
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
        var line = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = Space.M
        };
        line.Children.Add(Icon("wrench", IconSize.S).Themed(TextElement.ForegroundProperty, Muted));
        line.Children.Add(L(e.Tool ?? "tool", true).With(t =>
        {
            Grid.SetColumn(t, 1);
            t.VerticalAlignment = VerticalAlignment.Center;
        }));
        var args = new TextBlock
        {
            Text = TextDecode.Strip(e.Text.Split('\n')[0]),
            FontFamily = Theme.Mono,
            FontSize = Theme.MonoSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        }
            .Themed(TextBlock.ForegroundProperty, Muted);
        Grid.SetColumn(args, 2);
        line.Children.Add(args);
        // No arrow when opening would show the same one line the header already shows
        if (string.IsNullOrEmpty(e.Detail) || SameAsHeader(e.Detail, args.Text ?? ""))
        {
            return line.With(l => l.Margin = new Thickness(Space.L, Space.XXS));
        }
        // Full input (command, JSON arguments, patch) highlighted when opened
        return Lazy(new Expander
        {
            Header = line,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(Space.L, 0, 0, 0)
        }, () => Code(e.Detail, e.Lang ?? ".txt"));
    }

    /// <summary>Collapsed by default; long tool output and thinking stay out of the way. Highlighted when opened.</summary>
    private static Control Folded(string label, string text, string? lang, bool markdown = false) => Fold(label + " · ", text, lang, new Thickness(Space.L, 0, 0, 0), markdown);

    /// <summary>An expander whose header is the label and first line; a plain line when the text is that one line (opening would add nothing).</summary>
    private static Control Fold(string label, string text, string? lang, Thickness margin, bool markdown = false)
    {
        var first = FirstLine(TextDecode.Clean(text.Length > 400 ? text[..400] : text));
        var header = L(label + first, false, Muted).With(t =>
        {
            t.TextWrapping = TextWrapping.NoWrap;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
        });
        if (SameAsHeader(text, first))
        {
            return header.With(h => h.Margin = new Thickness(margin.Left + Space.L, Space.S, 0, Space.S));
        }

        return Lazy(new Expander
        {
            Header = header,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = margin
        }, () => markdown ? Rendered(TextDecode.Strip(text)) : StepBody(text, lang));
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
        var toggle = new Button
        {
            Content = "Show original",
            Padding = new Thickness(Space.M, Space.XXS),
            MinHeight = 0,
            FontSize = Size(TextSize.Caption),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        toggle.Click += (_, _) =>
        {
            formatted = !formatted;
            view.Content = Show(formatted);
            toggle.Content = formatted ? "Show original" : "Show formatted";
        };
        return new StackPanel
        {
            Spacing = Space.XS,
            Children = { toggle, view }
        };
    }

    /// <summary>Markdown in the body font; a Source toggle shows the text as written.</summary>
    private static StackPanel Rendered(string md)
    {
        var view = new ContentControl { Content = MarkdownView.Document(md) };
        var source = false;
        var toggle = new Button
        {
            Content = "Source",
            Padding = new Thickness(Space.M, Space.XXS),
            MinHeight = 0,
            FontSize = Size(TextSize.Caption),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        toggle.Click += (_, _) =>
        {
            source = !source;
            view.Content = source ? Code(md, ".md") : MarkdownView.Document(md);
            toggle.Content = source ? "Rendered" : "Source";
        };
        return new StackPanel
        {
            Spacing = Space.XS,
            Children = { toggle, view }
        };
    }

    private static SelectableTextBlock Plain(string text) =>
        new SelectableTextBlock
        {
            Text = text,
            FontFamily = Theme.Mono,
            FontSize = Theme.MonoSize,
            TextWrapping = TextWrapping.Wrap
        }.Themed(TextBlock.ForegroundProperty, Text);
}
