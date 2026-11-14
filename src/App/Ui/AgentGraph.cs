// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Time graph of a session with sub-agents: the main session on top, one lane per sub-agent below.
/// A thin line runs from where the main session started the agent (branch) to the agent's bar, and from the bar to where the main session picked the result up (merge).
/// The main bar changes color while at least one sub-agent runs (the main session waits or works alongside).
/// </summary>
internal static class AgentGraph
{
    public static Control Build(AgentTimeline t)
    {
        var total = Math.Max(1, (t.End - t.Start).TotalSeconds);
        double F(DateTime d) => Math.Clamp((d - t.Start).TotalSeconds / total, 0, 1);

        var rows = new StackPanel { Spacing = 5 };

        // Main session
        var main = new Grid { Height = 12 };
        double at = 0;
        void Seg(double from, double to, string key, string tip)
        {
            if (to - from <= 0)
            {
                return;
            }

            main.ColumnDefinitions.Add(new ColumnDefinition(to - from, GridUnitType.Star));
            var b = new Border().Themed(Border.BackgroundProperty, key);
            ToolTip.SetTip(b, tip);
            Grid.SetColumn(b, main.ColumnDefinitions.Count - 1);
            main.Children.Add(b);
        }
        foreach (var w in t.Waits)
        {
            Seg(at, F(w.From), ChartIn, "Main session");
            Seg(F(w.From), Math.Max(F(w.To), F(w.From) + .004), ChartOut, $"Sub-agent running {w.From:HH:mm:ss} – {w.To:HH:mm:ss}");
            at = Math.Max(F(w.To), F(w.From) + .004);
        }
        Seg(at, 1, ChartIn, "Main session");
        rows.Children.Add(Lane("Main", main));

        foreach (var l in t.Lanes)
        {
            double fk = F(l.Fork), s = F(l.Start), e = Math.Max(F(l.End), s + .004), mg = Math.Max(F(l.Merge), e);
            var g = new Grid { Height = 12 };
            void Col(double w, Control? c)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(w, 0.0001), GridUnitType.Star));
                if (c == null)
                {
                    return;
                }

                Grid.SetColumn(c, g.ColumnDefinitions.Count - 1);
                g.Children.Add(c);
            }
            static Control Line() => new Border { Height = 2, VerticalAlignment = VerticalAlignment.Center }.Themed(Border.BackgroundProperty, Muted);
            static Control Dotted(HorizontalAlignment h) => new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = h, VerticalAlignment = VerticalAlignment.Center }.Themed(Border.BackgroundProperty, Text);
            Col(fk, null);
            Col(s - fk, new Grid { Children = { Line(), Dotted(HorizontalAlignment.Left) } });
            Col(e - s, new Border { CornerRadius = new CornerRadius(3) }.Themed(Border.BackgroundProperty, ChartWrite));
            Col(mg - e, new Grid { Children = { Line(), Dotted(HorizontalAlignment.Right) } });
            Col(1 - mg, null);
            var dur = l.End - l.Start;
            ToolTip.SetTip(g, $"{l.Label}\n{l.Steps:N0} steps · {l.Start:HH:mm:ss} – {l.End:HH:mm:ss} ({(dur.TotalMinutes < 1 ? $"{dur.TotalSeconds:0} s" : $"{dur.TotalMinutes:0.#} min")})\nBranched {l.Fork:HH:mm:ss} · merged {l.Merge:HH:mm:ss}");
            rows.Children.Add(Lane(l.Label, g));
        }
        if (t.TotalLanes > t.Lanes.Count)
        {
            rows.Children.Add(Txt($"Showing {t.Lanes.Count} of {t.TotalLanes} sub-agents.", 12, false, Muted));
        }

        var legend = new WrapPanel
        {
            Children =
            {
                DotText("Main session", ChartIn).With(x => x.Margin = new Thickness(0, 0, 14, 0)),
                DotText("Main waiting for sub-agents", ChartOut).With(x => x.Margin = new Thickness(0, 0, 14, 0)),
                DotText("Sub-agent (line: branch ● … ● merge)", ChartWrite)
            }
        };
        var axis = Row(Txt(t.Start.ToString("HH:mm:ss"), 11, false, Muted), Txt("→", 11, false, Muted), Txt(t.End.ToString("HH:mm:ss"), 11, false, Muted));
        return Section("flow", "Sub-agents", new StackPanel { Spacing = 8, Children = { rows, axis, legend } });
    }

    private static Grid Lane(string label, Control bar)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 8 };
        var t = Txt(label, 12, false, Muted).With(x => { x.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; x.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis; x.VerticalAlignment = VerticalAlignment.Center; });
        Grid.SetColumn(bar, 1);
        g.Children.Add(t); g.Children.Add(bar);
        return g;
    }
}
