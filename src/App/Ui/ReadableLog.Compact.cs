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

/// <summary>
/// A conversation as one line per step (time, who, the text cut to the line, the cost); a click opens the step's full card in a popup:
/// rendered Markdown, highlighted code, tool input and output opened, its tokens and cost, and a Copy button.
/// </summary>
internal partial class ReadableLog
{
    private static Control CompactRow(LogEntry e)
    {
        var (icon, who, key) = e.Role switch
        {
            "user" => ("person", "You", Accent),
            "assistant" => ("bot", "Assistant", Text),
            "tool" => ("wrench", e.Tool ?? "Tool", Muted),
            "result" => ("document_text", "Result" + (e.Tool != null ? " · " + e.Tool : ""), Muted),
            "thinking" => ("brain_circuit", "Thinking", Muted),
            _ => ("info", "Context", Muted)
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Widths.LogTime},{Widths.LogWho},*,Auto"),
            ColumnSpacing = Space.L,
            VerticalAlignment = VerticalAlignment.Center
        };
        var time = Txt(e.Time?.ToString("HH:mm:ss") ?? "", TextSize.Caption, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        var name = Row(Icon(icon, IconSize.S).Themed(TextElement.ForegroundProperty, key), L(who, true, key).With(t => t.TextTrimming = TextTrimming.CharacterEllipsis));
        var line = OneLine(e);
        var text = Txt(line, TextSize.Body, false, e.Role is "user" or "assistant" ? Text : Muted).With(t =>
        {
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.VerticalAlignment = VerticalAlignment.Center;
        });
        Grid.SetColumn(name, 1);
        Grid.SetColumn(text, 2);
        grid.Children.Add(time);
        grid.Children.Add(name);
        grid.Children.Add(text);
        if (e.Usage != null && ModelCatalog.Cost(e.Usage) is { } cost)
        {
            var money = Txt(StepMoney(cost), TextSize.Caption, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center);
            Grid.SetColumn(money, 3);
            grid.Children.Add(money);
        }

        var row = new Border
        {
            Child = grid,
            MinHeight = Heights.LogRow,
            Padding = new Thickness(Space.L, Space.XS),
            BorderThickness = new Thickness(0, 0, 0, Stroke.Thin),
            Background = Brushes.Transparent,
            Cursor = Hand
        }.Themed(Border.BorderBrushProperty, BorderKey);
        row.PointerEntered += (_, _) => row.Themed(Border.BackgroundProperty, Subtle);
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, a) =>
        {
            if (a.InitialPressMouseButton == Avalonia.Input.MouseButton.Left)
            {
                ShowDetail(row, e);
            }
        };
        return row;
    }

    /// <summary>The step's text on one line: the first lines joined, extra white space gone.</summary>
    private static string OneLine(LogEntry e)
    {
        var text = e.Role == "tool" ? e.Text : TextDecode.Strip(e.Text);
        return string.Join(' ', text.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries).Take(60));
    }

    /// <summary>The step's full card in a popup under its row; tool input and output are open.</summary>
    internal static void ShowDetail(Control anchor, LogEntry e) =>
        new Flyout
        {
            Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft,
            Content = new ScrollViewer
            {
                Width = Widths.DialogL,
                MaxHeight = Heights.ViewerL,
                Content = DetailContent(anchor, e)
            }
        }.ShowAt(anchor);

    /// <summary>What the popup holds: a Copy button, the step's card (tool input and output opened) and its tokens and cost.</summary>
    internal static Control DetailContent(Control anchor, LogEntry e)
    {
        var body = Step(e);
        if (body is Expander x)
        {
            x.IsExpanded = true;
        }

        if (e.Usage != null)
        {
            body = WithUsage(body, e.Usage);
        }

        var copy = IconBtn("copy", "Copy this step as Markdown", () =>
        {
            if (TopLevel.GetTopLevel(anchor) is { } top)
            {
                Toast.Copy(top, SessionExport.Markdown(e), "Step as Markdown");
            }
        });
        copy.HorizontalAlignment = HorizontalAlignment.Right;
        return new StackPanel
        {
            Spacing = Space.S,
            Margin = new Thickness(Space.M),
            Children = { copy, body }
        };
    }
}
