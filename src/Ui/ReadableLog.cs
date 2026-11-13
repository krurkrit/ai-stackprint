using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using AiStackprint.Services;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>A parsed session shown as a conversation. Code in replies, tool inputs and tool results is syntax highlighted.</summary>
class ReadableLog
{
    public const int MaxSteps = 400;
    readonly Dictionary<LogEntry, Control> _controls = new();
    readonly Dictionary<LogEntry, Panel> _holders = new();   // each step's slot: the fading highlight is laid over it
    readonly ScrollViewer _scroll = new();
    public Control Root => _scroll;
    public List<LogEntry> Steps { get; }

    public ReadableLog(Session s, bool toEnd)
    {
        Steps = s.Entries.TakeLast(MaxSteps).ToList();
        var list = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        list.Children.Add(Summary(s));
        if (AgentTimelines.Build(s.Entries) is { } timeline) list.Children.Add(AgentGraph.Build(timeline));
        if (s.Entries.Count > MaxSteps) list.Children.Add(Txt($"Showing the last {MaxSteps} of {s.Entries.Count} steps.", 12, false, Muted));
        bool? wasSub = null;
        foreach (var e in Steps)
        {
            if (e.SubAgent && wasSub != true) list.Children.Add(Chip("Sub-agent", Accent).With(c => c.Margin = new Thickness(24, 6, 0, 0)));
            wasSub = e.SubAgent;
            var step = Step(e);
            _controls[e] = step;
            if (e.SubAgent)
                step = new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(12, 0, 0, 0), Margin = new Thickness(24, 0, 0, 0), Child = step }
                    .Themed(Border.BorderBrushProperty, Accent);
            var holder = new Panel { Children = { step } };
            _holders[e] = holder;
            list.Children.Add(holder);
        }
        if (Steps.Count == 0) list.Children.Add(Txt("No readable steps in this file.", 14, false, Muted));
        _scroll.Content = list;
        if (toEnd) Dispatcher.UIThread.Post(_scroll.ScrollToEnd, DispatcherPriority.Background);
    }

    /// <summary>Scrolls to a step and opens it if folded (search result click).</summary>
    public void Reveal(LogEntry e)
    {
        if (!_controls.TryGetValue(e, out var c)) return;
        if (c is Expander x) x.IsExpanded = true;
        Dispatcher.UIThread.Post(() => c.BringIntoView(), DispatcherPriority.Background);
    }

    /// <summary>Steps after <paramref name="previousLast"/> (the last step of the earlier read); none when it is no longer in view.</summary>
    public List<LogEntry> After(LogEntry? previousLast)
    {
        if (previousLast == null) return new();
        int i = Steps.LastIndexOf(previousLast);
        return i < 0 ? new() : Steps.Skip(i + 1).ToList();
    }

    /// <summary>Tints the given steps and fades the tint out over <paramref name="seconds"/>.</summary>
    public void Flash(IEnumerable<LogEntry> steps, double seconds)
    {
        if (seconds <= 0) return;
        foreach (var e in steps)
        {
            if (!_holders.TryGetValue(e, out var holder) || holder.Children.FirstOrDefault(c => c.IsHitTestVisible) is not { } step) continue;
            var margin = step.Margin;   // the step's own indent (20 px sides, sub-agent indent), so the highlight lines up with it
            // Fill behind the step (text keeps its contrast) and a ring on top (visible on opaque bubbles too)
            var fill = new Border { CornerRadius = new CornerRadius(8), Margin = margin, IsHitTestVisible = false }.Themed(Border.BackgroundProperty, HighlightKey);
            var ring = new Border { CornerRadius = new CornerRadius(8), Margin = margin, BorderThickness = new Thickness(2), IsHitTestVisible = false }.Themed(Border.BorderBrushProperty, HighlightRingKey);
            holder.Children.Insert(0, fill);
            holder.Children.Add(ring);
            foreach (var b in new[] { fill, ring })
                Dispatcher.UIThread.Post(() =>
                {
                    b.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(seconds), Easing = new Avalonia.Animation.Easings.CubicEaseIn() } };
                    b.Opacity = 0;
                }, DispatcherPriority.Background);
            DispatcherTimer.RunOnce(() => { holder.Children.Remove(fill); holder.Children.Remove(ring); }, TimeSpan.FromSeconds(seconds + 0.5));
        }
    }

    static Control Summary(Session s)
    {
        var w = new WrapPanel();
        void Add(string t) => w.Children.Add(LogChip(t));
        if (s.Models.Count > 0) Add(string.Join(", ", s.Models));
        Add(Plural(s.Entries.Count(e => e.Role == "user"), "prompt"));
        Add(Plural(s.Entries.Count(e => e.Role == "tool"), "tool call"));
        if (s.Entries.Any(e => e.SubAgent)) Add(Plural(s.Entries.Count(e => e.SubAgent), "sub-agent step"));
        if (s.InputTokens + s.OutputTokens > 0) Add($"{s.InputTokens:N0} in · {s.OutputTokens:N0} out tokens");
        if (s.Start is { } a && s.End is { } b)
            Add($"{a:yyyy-MM-dd HH:mm} – {b:HH:mm} ({((b - a).TotalMinutes < 1 ? $"{(b - a).TotalSeconds:0} s" : $"{(b - a).TotalMinutes:0} min")})");
        return w;
    }

    /// <summary>Conversation text in the body font at the Code &amp; logs size (<paramref name="delta"/> px smaller or larger).</summary>
    static TextBlock L(string s, bool bold = false, string key = W.Text, double delta = 0) =>
        new TextBlock { Text = s, FontSize = Ui.Theme.MonoSize + delta, FontWeight = bold ? Emphasis : Body, TextWrapping = TextWrapping.Wrap }.Themed(TextBlock.ForegroundProperty, key);

    static Control LogChip(string s) =>
        new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 4), Child = L(s, true), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 8) }
            .Themed(Border.BackgroundProperty, Subtle);

    public static string Plural(int n, string word) => $"{n:N0} {word}{(n == 1 ? "" : "s")}";

    static Control Step(LogEntry e) => e.Role switch
    {
        "user" => Message("person", "You", e, Selected, mine: true),
        "assistant" => Message("bot", "Assistant", e, Surface),
        "tool" => ToolCall(e),
        "result" => Folded("Result" + (e.Tool != null ? $" · {e.Tool}" : ""), e.Text, e.Lang),
        "thinking" => Folded("Thinking", e.Text, null, markdown: true),
        _ => Folded("Context", e.Text, null, markdown: true)
    };

    /// <param name="mine">Your own message: indented 20 px from the left; the assistant's keeps 20 px free on the right. Both read left to right.</param>
    static Control Message(string icon, string who, LogEntry e, string bg, bool mine = false)
    {
        var time = L(e.Time?.ToString("HH:mm:ss") ?? "", false, Muted, -1).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        var head = Row(Icon(icon, 16).Themed(TextElement.ForegroundProperty, Accent), L(who, true), time);
        var body = new StackPanel { Spacing = 6, Children = { head } };
        foreach (var part in ContextBlocks.Split(TextDecode.Strip(e.Text)))
        {
            if (part.Label != null) { body.Children.Add(Injected(part)); continue; }
            foreach (var (text, lang) in Fences(part.Text))
                body.Children.Add(lang == null ? MarkdownView.Render(text) : Code(text, lang));
        }
        return new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10), Child = body, Margin = mine ? new Thickness(20, 0, 0, 0) : new Thickness(0, 0, 20, 0) }
            .Themed(Border.BackgroundProperty, bg);
    }

    /// <summary>Text the app injected around the message (instructions, environment, reminders, marker blocks): its own collapsed area.</summary>
    static Control Injected(ContextBlocks.Part part) => Fold($"Injected · {part.Label} · ", part.Text, null, new Thickness(0), markdown: true);

    internal static Control Code(string text, string ext) =>
        new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = new CodeView(ext, lineNumbers: false, compact: true) { Text = text.TrimEnd('\n') } };

    static readonly Regex Fence = new(@"```([\w+#.-]*)[^\n]*\n(.*?)```", RegexOptions.Singleline);

    /// <summary>Splits markdown into prose and fenced code blocks; the fence language picks the grammar.</summary>
    internal static IEnumerable<(string Text, string? Lang)> Fences(string md)
    {
        int at = 0;
        foreach (Match m in Fence.Matches(md))
        {
            if (m.Index > at && md[at..m.Index].Trim() != "") yield return (md[at..m.Index], null);
            yield return (m.Groups[2].Value, Ext(m.Groups[1].Value));
            at = m.Index + m.Length;
        }
        if (at < md.Length && md[at..].Trim() != "") yield return (md[at..], null);
    }

    static string Ext(string fence) => fence.ToLowerInvariant() switch
    {
        "" or "text" or "txt" or "plain" => ".txt", "csharp" or "cs" or "c#" => ".cs", "js" or "javascript" => ".js", "ts" or "typescript" => ".ts",
        "bash" or "sh" or "shell" or "zsh" or "console" => ".sh", "powershell" or "ps1" or "pwsh" => ".ps1", "python" or "py" => ".py",
        "yaml" or "yml" => ".yaml", "toml" => ".ini", "markdown" or "md" => ".md", "rust" or "rs" => ".rs", "golang" or "go" => ".go", var x => "." + x
    };

    static Control ToolCall(LogEntry e)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8 };
        line.Children.Add(Icon("wrench", 14).Themed(TextElement.ForegroundProperty, Muted));
        line.Children.Add(L(e.Tool ?? "tool", true).With(t => { Grid.SetColumn(t, 1); t.VerticalAlignment = VerticalAlignment.Center; }));
        var args = new TextBlock { Text = TextDecode.Strip(e.Text.Split('\n')[0]), FontFamily = Ui.Theme.Mono, FontSize = Ui.Theme.MonoSize, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, Muted);
        Grid.SetColumn(args, 2); line.Children.Add(args);
        // No arrow when opening would show the same one line the header already shows
        if (string.IsNullOrEmpty(e.Detail) || SameAsHeader(e.Detail, args.Text ?? "")) return line.With(l => l.Margin = new Thickness(12, 2));
        // Full input (command, JSON arguments, patch) highlighted when opened
        return Lazy(new Expander { Header = line, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(12, 0, 0, 0) }, () => Code(e.Detail, e.Lang ?? ".txt"));
    }

    /// <summary>Collapsed by default; long tool output and thinking stay out of the way. Highlighted when opened.</summary>
    static Control Folded(string label, string text, string? lang, bool markdown = false) => Fold(label + " · ", text, lang, new Thickness(12, 0, 0, 0), markdown);

    /// <summary>An expander whose header is the label and first line; a plain line when the text is that one line (opening would add nothing).</summary>
    static Control Fold(string label, string text, string? lang, Thickness margin, bool markdown = false)
    {
        var first = FirstLine(TextDecode.Clean(text.Length > 400 ? text[..400] : text));
        var header = L(label + first, false, Muted).With(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; });
        if (SameAsHeader(text, first)) return header.With(h => h.Margin = new Thickness(margin.Left + 12, 6, 0, 6));
        return Lazy(new Expander { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = margin }, () => markdown ? Rendered(TextDecode.Strip(text)) : StepBody(text, lang));
    }

    /// <summary>The opened text would equal the header line: one short line that decoding does not change.</summary>
    static bool SameAsHeader(string text, string header)
    {
        var t = text.Trim();
        return t.Length <= 120 && !t.Contains('\n') && t == header.Trim() && !TextDecode.Prepare(t, null).Changed;
    }

    /// <summary>Opened content: decoded text (JSON pretty, escapes, shell colors, image data shortened) with a toggle back to the original when it was changed.</summary>
    static Control StepBody(string original, string? lang)
    {
        if (lang is ".md" or ".markdown") return Rendered(original);
        var p = TextDecode.Prepare(original, lang);
        Control Show(bool fmt) => !fmt ? Plain(original) : p.Ansi != null ? AnsiText(p.Ansi) : p.Lang != null ? Code(p.Text, p.Lang) : Plain(p.Text);
        var view = new ContentControl { Content = Show(true) };
        if (!p.Changed) return view;
        bool formatted = true;
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
    static Control Rendered(string md)
    {
        var view = new ContentControl { Content = MarkdownView.Document(md) };
        bool source = false;
        var toggle = new Button { Content = "Source", Padding = new Thickness(8, 2), MinHeight = 0, FontSize = Size(12), HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Click += (_, _) =>
        {
            source = !source;
            view.Content = source ? Code(md, ".md") : MarkdownView.Document(md);
            toggle.Content = source ? "Rendered" : "Source";
        };
        return new StackPanel { Spacing = 4, Children = { toggle, view } };
    }

    static Control Plain(string text) =>
        new SelectableTextBlock { Text = text, FontFamily = Ui.Theme.Mono, FontSize = Ui.Theme.MonoSize, TextWrapping = TextWrapping.Wrap }.Themed(SelectableTextBlock.ForegroundProperty, Text);

    /// <summary>Shell output with its colors. Palette follows the theme at the time the step is opened.</summary>
    static Control AnsiText(List<TextDecode.AnsiRun> runs)
    {
        var tb = new SelectableTextBlock { FontFamily = Ui.Theme.Mono, FontSize = Ui.Theme.MonoSize, TextWrapping = TextWrapping.Wrap }.Themed(SelectableTextBlock.ForegroundProperty, Text);
        bool dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        tb.Inlines ??= new InlineCollection();
        foreach (var r in runs)
        {
            var run = new Run(r.Text);
            if (r.Color is { } c) run.Foreground = new ImmutableSolidColorBrush(AnsiColor(c, dark));
            if (r.Bold) run.FontWeight = FontWeight.Bold;
            tb.Inlines.Add(run);
        }
        return tb;
    }

    static readonly string[] AnsiDark =
        { "#6E7681", "#F47067", "#57AB5A", "#C69026", "#539BF5", "#B083F0", "#39C5CF", "#ADBAC7", "#909DAB", "#FF938A", "#6BC46D", "#DAAA3F", "#6CB6FF", "#DCBDFB", "#56D4DD", "#CDD9E5" };
    static readonly string[] AnsiLight =
        { "#24292F", "#CF222E", "#116329", "#7D4E00", "#0550AE", "#8250DF", "#1B7C83", "#6E7781", "#57606A", "#A40E26", "#1A7F37", "#633C01", "#0969DA", "#6639BA", "#3192AA", "#8C959F" };

    static Color AnsiColor(int c, bool dark) =>
        c >= 0x1000000 ? Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c) : Color.Parse((dark ? AnsiDark : AnsiLight)[Math.Clamp(c, 0, 15)]);

    /// <summary>Builds the expander's content the first time it opens, so hundreds of steps stay fast.</summary>
    static Expander Lazy(Expander x, Func<Control> content)
    {
        x.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty && x.IsExpanded && x.Content == null) x.Content = content(); };
        return x;
    }

    static string FirstLine(string s) { var l = s.TrimStart().Split('\n')[0]; return l.Length > 120 ? l[..120] + "…" : l; }
}
