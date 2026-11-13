// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class LogViewer
{
    private Regex? Pattern()
    {
        if (string.IsNullOrEmpty(_query.Text))
        {
            return null;
        }

        try { return LogSearch.Pattern(_query.Text, _case.IsChecked == true, _regex.IsChecked == true); }
        catch (ArgumentException) { return null; }
    }

    private readonly List<SearchHit> _hits = [];

    /// <summary>Raw: searches the whole file in the background. Conversation: searches the steps. Results go in the side panel.</summary>
    private async void Search()
    {
        _search?.Cancel();
        _hits.Clear();
        var p = Pattern();
        if (p == null || _file == null) { _results.Content = string.IsNullOrEmpty(_query.Text) ? null : Panel(Txt("Invalid pattern.", 12, false, Muted)); if (_only.IsChecked == true) { Render(false); } return; }
        if (ShowReadable)
        {
            var steps = _readable?.Steps.Where(e => p.IsMatch($"{e.Tool} {e.Text} {e.Detail}")).ToList() ?? [];
            _results.Content = Panel(Txt(ReadableLog.Plural(steps.Count, "step"), 12, true),
                steps.Take(500).Select(e => Hit($"{e.Time:HH:mm:ss} {e.Role}", Snip(e.Text), () => _readable!.Reveal(e))));
            return;
        }
        var cts = _search = new CancellationTokenSource();
        var path = _file.Path;
        _results.Content = Panel(Busy.Loading("Searching the whole file…", 14));
        try
        {
            var hits = await Task.Run(() => LogSearch.File(path, p, LogSearch.MaxHits, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _hits.AddRange(hits);
            _results.Content = Panel(Txt(hits.Count >= LogSearch.MaxHits ? $"First {LogSearch.MaxHits} matches" : $"{hits.Count:N0} {(hits.Count == 1 ? "match" : "matches")}", 12, true),
                hits.Select(h => Hit($"Line {h.Line:N0}", h.Text, () => Jump(h))));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _results.Content = Panel(Txt("Cannot read: " + e.Message, 12, false, Muted)); }
        if (_only.IsChecked == true)
        {
            Render(false);
        }
    }

    /// <summary>Scrolls the raw view to a hit; loads the part of the file around it when it is outside what is shown.</summary>
    private void Jump(SearchHit h)
    {
        if (_code == null || _file == null)
        {
            return;
        }

        var lastShown = _firstLine + _raw.Count(c => c == '\n');
        if (_only.IsChecked == true) { _only.IsChecked = false; Render(false); }
        if (h.Line < _firstLine || h.Line > lastShown)
        {
            try { (_raw, _firstLine) = LogSearch.Window(_file.Path, h.Line); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = "Cannot read: " + e.Message; return; }
            _window = true; Follow = false;
            _status.Text = $"Showing lines {_firstLine:N0}–{_firstLine + _raw.Count(c => c == '\n') - 1:N0} around the match";
            Render(false);
        }
        var line = (int)(h.Line - _firstLine + 1);
        Dispatcher.UIThread.Post(() => _code.Reveal(line, h.Column, h.Length), DispatcherPriority.Background);
    }

    private static string Snip(string s) { var l = s.Replace('\n', ' ').Trim(); return l.Length > 140 ? l[..140] + "…" : l; }

    private static Control Panel(Control head, IEnumerable<Control>? rows = null)
    {
        var sp = new StackPanel { Spacing = 2, Children = { head.With(h => h.Margin = new Thickness(4, 0, 0, 6)) } };
        if (rows != null)
        {
            sp.Children.AddRange(rows);
        }

        return new Border { Width = 300, CornerRadius = new CornerRadius(6), Padding = new Thickness(6), Child = new ScrollViewer { Content = sp } }.Themed(Border.BackgroundProperty, Background);
    }

    private static Control Hit(string where, string text, Action go)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4),
            Cursor = Hand,
            Background = Avalonia.Media.Brushes.Transparent,
            Child = new StackPanel
            {
                Children = { Txt(where, 12, false, Accent).With(t => { t.FontFamily = Theme.Mono; t.FontSize = Theme.MonoSize; }),
                Txt(text, 12).With(t => { t.FontFamily = Theme.Mono; t.FontSize = Theme.MonoSize; t.TextWrapping = Avalonia.Media.TextWrapping.Wrap; t.MaxLines = 3; }) }
            }
        };
        b.PointerEntered += (_, _) => b.Themed(Border.BackgroundProperty, Subtle);
        b.PointerExited += (_, _) => b.Background = Avalonia.Media.Brushes.Transparent;
        b.PointerReleased += (_, _) => go();
        return b;
    }

    public static string Size(long b) => b < 1024 ? $"{b} B" : b < 1 << 20 ? $"{b / 1024.0:0.#} KB" : $"{b / 1048576.0:0.#} MB";
}
