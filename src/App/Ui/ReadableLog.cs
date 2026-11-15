// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>A parsed session shown as a conversation. Code in replies, tool inputs and tool results is syntax highlighted.</summary>
internal partial class ReadableLog
{
    public const int MaxSteps = 400;
    private readonly Dictionary<LogEntry, Control> _controls = [];
    private readonly Dictionary<LogEntry, Panel> _holders = [];   // each step's slot: the fading highlight is laid over it
    private readonly ScrollViewer _scroll = new();
    public Control Root => _scroll;
    public List<LogEntry> Steps { get; }

    /// <summary>Steps built when a file opens; earlier ones are built when asked for ("Show earlier steps", a search hit), so a long session opens fast.</summary>
    public const int FirstSteps = 60;
    private const int MoreSteps = 100;

    private readonly StackPanel _list = new() { Spacing = Space.M, Margin = new Thickness(Space.XL) };
    private int _built;           // steps built, counted from the end of Steps
    private readonly int _headerCount;   // controls above the first step (summary, graph, notes)
    private Button? _earlier;

    public ReadableLog(Session s, bool toEnd)
    {
        Steps = [.. s.Entries.TakeLast(MaxSteps)];
        _list.Children.Add(Summary(s));
        if (AgentTimelines.Build(s.Entries) is { } timeline)
        {
            _list.Children.Add(AgentGraph.Build(timeline));
        }

        if (s.Entries.Count > MaxSteps)
        {
            _list.Children.Add(Txt($"Showing the last {MaxSteps} of {s.Entries.Count} steps.", TextSize.Caption, false, Muted));
        }

        _headerCount = _list.Children.Count;
        BuildEarlier(FirstSteps);
        if (Steps.Count == 0)
        {
            _list.Children.Add(Txt("No readable steps in this file.", TextSize.Body, false, Muted));
        }

        _scroll.Content = _list;
        if (toEnd)
        {
            Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
        }
    }

    /// <summary>Builds up to <paramref name="count"/> more steps before the ones already shown, and keeps a "Show earlier steps" button above them while some are left.</summary>
    private void BuildEarlier(int count)
    {
        var to = Steps.Count - _built;
        var from = Math.Max(0, to - count);
        if (_earlier != null)
        {
            _list.Children.Remove(_earlier);
            _earlier = null;
        }

        var at = _headerCount;
        bool? wasSub = from > 0 ? Steps[from - 1].SubAgent : null;
        for (var i = from; i < to; i++)
        {
            var e = Steps[i];
            if (e.SubAgent && wasSub != true)
            {
                _list.Children.Insert(at++, Chip("Sub-agent", Accent).With(c => c.Margin = new Thickness(Space.XXXL, Space.S, 0, 0)));
            }

            wasSub = e.SubAgent;
            var step = Step(e);
            _controls[e] = step;
            if (e.SubAgent)
            {
                step = new Border { BorderThickness = new Thickness(Space.XXS, 0, 0, 0), Padding = new Thickness(Space.L, 0, 0, 0), Margin = new Thickness(Space.XXXL, 0, 0, 0), Child = step }
                    .Themed(Border.BorderBrushProperty, Accent);
            }

            var holder = new Panel { Children = { step } };
            _holders[e] = holder;
            _list.Children.Insert(at++, holder);
        }

        _built += to - from;
        if (from > 0)
        {
            _earlier = Btn($"Show {Math.Min(MoreSteps, from)} earlier steps ({from} not shown)", () => BuildEarlier(MoreSteps));
            _earlier.HorizontalAlignment = HorizontalAlignment.Center;
            _list.Children.Insert(_headerCount, _earlier);
        }
    }

    /// <summary>Scrolls to a step and opens it if folded (search result click).</summary>
    public void Reveal(LogEntry e)
    {
        var i = Steps.IndexOf(e);
        if (i >= 0 && i < Steps.Count - _built)
        {
            BuildEarlier(Steps.Count - _built - i);   // a search hit above what is built: build down to it
        }

        if (!_controls.TryGetValue(e, out var c))
        {
            return;
        }

        if (c is Expander x)
        {
            x.IsExpanded = true;
        }

        Dispatcher.UIThread.Post(c.BringIntoView, DispatcherPriority.Background);
    }

    /// <summary>Steps after <paramref name="previousLast"/> (the last step of the earlier read); none when it is no longer in view.</summary>
    public List<LogEntry> After(LogEntry? previousLast)
    {
        if (previousLast == null)
        {
            return [];
        }

        var i = Steps.LastIndexOf(previousLast);
        return i < 0 ? [] : [.. Steps.Skip(i + 1)];
    }

    /// <summary>Tints the given steps and fades the tint out over <paramref name="seconds"/>.</summary>
    public void Flash(IEnumerable<LogEntry> steps, double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        foreach (var e in steps)
        {
            if (!_holders.TryGetValue(e, out var holder) || holder.Children.FirstOrDefault(c => c.IsHitTestVisible) is not { } step)
            {
                continue;
            }

            var margin = step.Margin;   // the step's own indent (20 px sides, sub-agent indent), so the highlight lines up with it
            // Fill behind the step (text keeps its contrast) and a ring on top (visible on opaque bubbles too)
            var fill = new Border { CornerRadius = new CornerRadius(Radius.L), Margin = margin, IsHitTestVisible = false }.Themed(Border.BackgroundProperty, HighlightKey);
            var ring = new Border { CornerRadius = new CornerRadius(Radius.L), Margin = margin, BorderThickness = new Thickness(Stroke.Medium), IsHitTestVisible = false }.Themed(Border.BorderBrushProperty, HighlightRingKey);
            holder.Children.Insert(0, fill);
            holder.Children.Add(ring);
            foreach (var b in new[] { fill, ring })
            {
                Dispatcher.UIThread.Post(() =>
                {
                    b.Transitions = [new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(seconds), Easing = new Avalonia.Animation.Easings.CubicEaseIn() }];
                    b.Opacity = 0;
                }, DispatcherPriority.Background);
            }

            DispatcherTimer.RunOnce(() => { holder.Children.Remove(fill); holder.Children.Remove(ring); }, TimeSpan.FromSeconds(seconds + 0.5));
        }
    }

    private static WrapPanel Summary(Session s)
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

        if (s.InputTokens + s.OutputTokens > 0)
        {
            Add($"{s.InputTokens:N0} in · {s.OutputTokens:N0} out tokens");
        }

        if (s.Start is { } a && s.End is { } b)
        {
            Add($"{a:yyyy-MM-dd HH:mm} – {b:HH:mm} ({((b - a).TotalMinutes < 1 ? $"{(b - a).TotalSeconds:0} s" : $"{(b - a).TotalMinutes:0} min")})");
        }

        return w;
    }

    /// <summary>Conversation text in the body font at the Code &amp; logs size (<paramref name="delta"/> px smaller or larger).</summary>
    private static TextBlock L(string s, bool bold = false, string key = Text, double delta = 0) =>
        new TextBlock { Text = s, FontSize = Theme.MonoSize + delta, FontWeight = bold ? Emphasis : Body, TextWrapping = TextWrapping.Wrap }.Themed(TextBlock.ForegroundProperty, key);

    private static Border LogChip(string s) =>
        new Border { CornerRadius = new CornerRadius(Radius.Pill), Padding = new Thickness(Space.L, Space.XS), Child = L(s, true), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, Space.M, Space.M) }
            .Themed(Border.BackgroundProperty, Subtle);

    public static string Plural(int n, string word) => $"{n:N0} {word}{(n == 1 ? "" : "s")}";

    private static Control Step(LogEntry e) => e.Role switch
    {
        "user" => Message("person", "You", e, Selected, mine: true),
        "assistant" => Message("bot", "Assistant", e, Surface),
        "tool" => ToolCall(e),
        "result" => Folded("Result" + (e.Tool != null ? $" · {e.Tool}" : ""), e.Text, e.Lang),
        "thinking" => Folded("Thinking", e.Text, null, markdown: true),
        _ => Folded("Context", e.Text, null, markdown: true)
    };

    /// <param name="mine">Your own message: indented 20 px from the left; the assistant's keeps 20 px free on the right. Both read left to right.</param>
    private static Border Message(string icon, string who, LogEntry e, string bg, bool mine = false)
    {
        var time = L(e.Time?.ToString("HH:mm:ss") ?? "", false, Muted, -1).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        var head = Row(Icon(icon, IconSize.S).Themed(TextElement.ForegroundProperty, Accent), L(who, true), time);
        var body = new StackPanel { Spacing = Space.S, Children = { head } };
        foreach (var part in ContextBlocks.Split(TextDecode.Strip(e.Text)))
        {
            if (part.Label != null) { body.Children.Add(Injected(part)); continue; }
            foreach (var (text, lang) in Fences(part.Text))
            {
                body.Children.Add(lang == null ? MarkdownView.Render(text) : Code(text, lang));
            }
        }
        return new Border { CornerRadius = new CornerRadius(Radius.L), Padding = new Thickness(Space.L, Space.L), Child = body, Margin = mine ? new Thickness(Space.XXL, 0, 0, 0) : new Thickness(0, 0, Space.XXL, 0) }
            .Themed(Border.BackgroundProperty, bg);
    }
}
