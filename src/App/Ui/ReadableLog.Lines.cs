// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Text logs (<see cref="LineLogs"/>): one compact row per line: time, level, message; the lines that followed it (a stack trace) under the message.</summary>
internal partial class ReadableLog
{
    /// <summary>Lines of a text log are small: many more fit than conversation steps.</summary>
    internal const int MaxLines = 5000;

    private static bool IsLines(Session s) => s.Entries.Count > 0 && LineLogs.IsLevel(s.Entries[0].Role);

    private static Control LogLine(LogEntry e)
    {
        var color = e.Role switch
        {
            "error" => ErrorKey,
            "warn" => WarningKey,
            "debug" or "trace" => Muted,
            _ => Text
        };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"Auto,{Widths.LogLevel},*"),
            ColumnSpacing = Space.L,
            Margin = new Thickness(0, 0, 0, Space.XXS)
        };
        static Control Mono(string text, string key, bool bold = false) => Txt(text, TextSize.Small, bold, key).With(t =>
        {
            t.FontFamily = Theme.Mono;
            t.FontSize = Theme.MonoSize;
        });
        var time = Mono(e.Time?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", Muted);
        var level = Mono(e.Role.ToUpperInvariant(), color, e.Role is "error" or "warn");
        var message = Mono(e.Text, color).With(t =>
        {
            if (t is TextBlock b)
            {
                b.TextWrapping = TextWrapping.Wrap;
                b.MaxLines = 40;
                b.TextTrimming = TextTrimming.CharacterEllipsis;
            }
        });
        Grid.SetColumn(level, 1);
        Grid.SetColumn(message, 2);
        row.Children.Add(time);
        row.Children.Add(level);
        row.Children.Add(message);
        return row;
    }

    private readonly bool _lines;
    private readonly HashSet<string> _hidden = [];   // levels the chips turned off

    /// <summary>Header of a text log: lines and the time span, and a chip per level (with its count) that shows or hides those lines.</summary>
    private WrapPanel LinesSummary(Session s)
    {
        var w = new WrapPanel();
        void Add(string t) => w.Children.Add(LogChip(t));
        Add(Plural(s.Entries.Count, "line"));
        foreach (var level in s.Entries.GroupBy(e => e.Role).OrderBy(g => Array.IndexOf(["error", "warn", "info", "debug", "trace", "log"], g.Key)))
        {
            var role = level.Key;
            var chip = new FilterChip(L($"{char.ToUpperInvariant(role[0])}{role[1..]} {level.Count():N0}", true), $"Show or hide the {role} lines", on =>
            {
                if (on)
                {
                    _hidden.Remove(role);
                }
                else
                {
                    _hidden.Add(role);
                }

                foreach (var (e, holder) in _holders)
                {
                    holder.IsVisible = !_hidden.Contains(e.Role);
                }
            });
            w.Children.Add(chip);
        }

        if (s.Start is { } a && s.End is { } b)
        {
            Add($"{a:yyyy-MM-dd HH:mm} – {b:yyyy-MM-dd HH:mm}");
        }

        return w;
    }
}
