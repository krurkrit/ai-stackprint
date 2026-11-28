// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>What the summary can do about sub-agents outside the part shown: load the stretch around the newest earlier one, or go back to the end of the session.</summary>
internal sealed record AgentsNav(Action? Earlier, Action? Back);

internal partial class ReadableLog
{
    private static WrapPanel Summary(Session s, AgentsNav? nav)
    {
        var w = new WrapPanel();
        void Add(string t) => w.Children.Add(LogChip(t));
        if (s.Models.Count > 0)
        {
            Add(string.Join(", ", s.Models));
        }

        Add(Plural(s.Entries.Count(e => e.Role == "user"), "prompt"));
        Add(Plural(s.Entries.Count(e => e.Role == "tool"), "tool call"));
        if (s.Entries.Any(e => e.SubAgent))
        {
            Add(Plural(s.Entries.Count(e => e.SubAgent), "sub-agent step"));
        }

        if (s.AgentsBefore > 0)
        {
            Control chip = nav?.Earlier is { } earlier
                ? Btn(Plural(s.AgentsBefore, "sub-agent") + " earlier: show", earlier, false, "link_arrow").With(b => b.Margin = new Thickness(0, 0, Space.M, Space.M))
                : LogChip(Plural(s.AgentsBefore, "sub-agent") + " earlier");
            ToolTip.SetTip(chip, "Their files are in the session's subagents folder; their steps come before the part of this long session that is shown. Show loads the part of the file around the newest of them.");
            w.Children.Add(chip);
        }

        if (nav?.Back is { } back)
        {
            w.Children.Add(Btn("Back to the end", back, false, "arrow_reset").With(b => b.Margin = new Thickness(0, 0, Space.M, Space.M)));
        }

        if (s.InputTokens + s.OutputTokens > 0)
        {
            Add($"{s.InputTokens:N0} in · {s.OutputTokens:N0} out tokens");
        }

        if (SessionExport.TouchedFiles(s.Entries) is { Count: > 0 } files)
        {
            var chip = LogChip(Plural(files.Count, "file") + " changed");
            ToolTip.SetTip(chip, string.Join("\n", files.Take(20)) + (files.Count > 20 ? $"\n… and {files.Count - 20} more" : ""));
            w.Children.Add(chip);
        }

        if (s.Entries.Where(e => e.Usage != null).Select(e => ModelCatalog.Cost(e.Usage!)).Where(c => c != null).Sum() is > 0 and var total)
        {
            Add("≈ " + StepMoney(total) + " estimated");
        }

        if (s.Entries.Count(e => e.IsError) is > 0 and var failed)
        {
            w.Children.Add(Chip(Plural(failed, "failed result"), ErrorMark).With(c => c.Margin = new Thickness(0, 0, Space.M, Space.M)));
        }

        if (PeakContext(s) is { } peak)
        {
            Add(peak);
        }

        if (s.Start is { } a && s.End is { } b)
        {
            Add($"{a:yyyy-MM-dd HH:mm} – {b:HH:mm} ({((b - a).TotalMinutes < 1 ? $"{(b - a).TotalSeconds:0} s" : $"{(b - a).TotalMinutes:0} min")})");
        }

        return w;
    }

    /// <summary>"Peak context 312K of 1M (31%)": the largest request against the window the log states, else the model data's; null when unknown.</summary>
    internal static string? PeakContext(Session s)
    {
        if (s.PeakContext <= 0)
        {
            return null;
        }

        var window = s.ContextWindow > 0 ? s.ContextWindow : s.Models.Select(m => ModelCatalog.Info(m)?.ContextWindow ?? 0).DefaultIfEmpty(0).Max();
        return window > 0
            ? $"Peak context {UsageCard.Fmt(s.PeakContext)} of {UsageCard.Fmt(window)} ({100.0 * s.PeakContext / window:0}%)"
            : $"Peak context {UsageCard.Fmt(s.PeakContext)}";
    }

    /// <summary>Conversation text in the body font at the Code &amp; logs size (<paramref name="delta"/> px smaller or larger).</summary>
    private static TextBlock L(string s, bool bold = false, string key = Text, double delta = 0) =>
        new TextBlock
        {
            Text = s,
            FontSize = Theme.MonoSize + delta,
            FontWeight = bold ? Emphasis : Body,
            TextWrapping = TextWrapping.Wrap
        }.Themed(TextBlock.ForegroundProperty, key);

    private static Border LogChip(string s) =>
        new Border
        {
            CornerRadius = new CornerRadius(Radius.Pill),
            Padding = new Thickness(Space.L, Space.XS),
            Child = L(s, true),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, Space.M, Space.M)
        }
            .Themed(Border.BackgroundProperty, Subtle);

    public static string Plural(int n, string word) => $"{n:N0} {word}{(n == 1 ? "" : "s")}";

    private static Control Step(LogEntry e) => e.Role switch
    {
        _ when LineLogs.IsLevel(e.Role) => LogLine(e),
        "user" => Message("person", "You", e, Selected, mine: true),
        "assistant" => Message("bot", "Assistant", e, Surface),
        "tool" => ToolCall(e),
        "result" => ResultCard(e),
        "thinking" => Folded("Thinking", e.Text, null, markdown: true),
        _ => Folded("Context", e.Text, null, markdown: true)
    };

    /// <param name="mine">Your own message: indented 20 px from the left; the assistant's keeps 20 px free on the right. Both read left to right.</param>
    private static Border Message(string icon, string who, LogEntry e, string bg, bool mine = false)
    {
        var time = L(e.Time?.ToString("HH:mm:ss") ?? "", false, Muted, -1).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        var head = Row(Icon(icon, IconSize.S).Themed(TextElement.ForegroundProperty, Accent), L(who, true), time);
        var body = new StackPanel
        {
            Spacing = Space.S,
            Children = { head }
        };
        foreach (var part in ContextBlocks.Split(TextDecode.Strip(e.Text)))
        {
            if (part.Label != null)
            {
                body.Children.Add(Injected(part));
                continue;
            }
            foreach (var (text, lang) in Fences(part.Text))
            {
                body.Children.Add(lang == null ? MarkdownView.Render(text) : Code(text, lang));
            }
        }
        return new Border
        {
            CornerRadius = new CornerRadius(Radius.L),
            Padding = new Thickness(Space.L, Space.L),
            Child = body,
            Margin = mine ? new Thickness(Space.XXL, 0, 0, 0) : new Thickness(0, 0, Space.XXL, 0)
        }
            .Themed(Border.BackgroundProperty, bg);
    }
}

