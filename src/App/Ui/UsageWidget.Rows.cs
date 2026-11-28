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

/// <summary>The widget's content: the header (time of the newest reading, gear, ×), one block per app and one row per limit window.</summary>
internal sealed partial class UsageWidget
{
    /// <summary>The gap above and below the line between two app blocks.</summary>
    private const double BlockGap = Space.M + Space.XXS;

    private TextBlock? _time;

    /// <summary>The header, built once so the gear's pop-up stays open while the rows are built again: the time of the newest reading, the gear and ×.</summary>
    private DockPanel Header()
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, Space.S) };
        _gear = SmallBtn("settings", "Widget settings", OpenSettings);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.M,
            Children = { _gear, SmallBtn("dismiss", "Hide the widget (Show widget in the tray menu brings it back)", () => _h.ShowWidget(false)) }
        };
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        _time = Txt("Usage limits", TextSize.Caption, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        head.Children.Add(_time);
        return head;
    }

    /// <summary>One block per app, or how to turn limits on when there is none.</summary>
    public Control Rows(IReadOnlyList<WidgetApp> apps)
    {
        _time?.Text = apps.Count > 0 ? $"Usage limits · now {WidgetRows.Time(apps.Max(a => a.At))}" : "Usage limits";

        var sp = new StackPanel();
        if (apps.Count == 0)
        {
            sp.Children.Add(Txt("Turn on live limits in Settings › Usage limits", TextSize.Small, false, Muted));
            sp.Children.Add(LinkBtn("Open Settings", () => _h.Open("Settings:Usage limits")));
            return sp;
        }

        for (var i = 0; i < apps.Count; i++)
        {
            sp.Children.Add(Block(apps[i], i == 0));
        }

        return sp;
    }

    private Control Block(WidgetApp a, bool first)
    {
        var title = new DockPanel();
        var right = Txt(a.Stale ? $"as of {WidgetRows.Time(a.At)}" : a.Plan ?? "", TextSize.Caption, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center);
        DockPanel.SetDock(right, Dock.Right);
        title.Children.Add(right);
        title.Children.Add(new Border
        {
            Background = Brushes.Transparent,
            Cursor = Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = Txt(a.Name, TextSize.Small, true)
        }.With(b => b.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            _h.Open("App:" + a.Id);
        }).With(b => b.PointerPressed += (_, e) => e.Handled = true));   // a click opens the app's page instead of moving the widget
        var sp = new StackPanel { Children = { title } };
        foreach (var r in a.Rows)
        {
            sp.Children.Add(Row(r, a.Stale));
        }

        if (a.Stale && a.Status != null)
        {
            sp.Children.Add(Txt(a.Status, TextSize.Caption, false, Muted).With(t => t.Margin = new Thickness(0, Space.S, 0, 0)));
        }

        return first ? sp : new Border
        {
            BorderThickness = new Thickness(0, Stroke.Thin, 0, 0),
            Margin = new Thickness(0, BlockGap, 0, 0),
            Padding = new Thickness(0, BlockGap, 0, 0),
            Child = sp
        }.Themed(Border.BorderBrushProperty, BorderKey);
    }

    /// <summary>The window name, its bar with the even-pace mark, the percent, and the line under them.</summary>
    private static StackPanel Row(WidgetRow r, bool stale)
    {
        var g = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Widths.WidgetLabel},*,{Widths.WidgetPercent}"),
            ColumnSpacing = Space.M,
            Margin = new Thickness(0, Space.M, 0, 0)
        };
        g.Children.Add(Txt(r.Window, TextSize.Small, false, Muted).With(t =>
        {
            t.VerticalAlignment = VerticalAlignment.Center;
            t.TextWrapping = TextWrapping.NoWrap;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
        }));
        g.Children.Add(new WidgetBar(r.Used, r.Pace).With(b => Grid.SetColumn(b, 1)));
        g.Children.Add(Txt($"{Math.Round(r.Used):0}%", TextSize.Small, true).With(t =>
        {
            t.HorizontalAlignment = HorizontalAlignment.Right;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 2);
        }));
        var sp = new StackPanel { Children = { g } };
        if (r.Line.Length > 0)
        {
            var warns = r.Warns && !stale;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.XS, Margin = new Thickness(Widths.WidgetIndent, Sizes.PaceMarkRise, 0, 0) };
            if (warns)
            {
                line.Children.Add(Icon("warning", IconSize.XS).Themed(TextElement.ForegroundProperty, WarningKey).With(i => i.VerticalAlignment = VerticalAlignment.Center));
            }

            line.Children.Add(Txt(r.Line, TextSize.Caption, false, warns ? WarningKey : Muted));
            sp.Children.Add(line);
        }

        return sp;
    }

    private static Button SmallBtn(string icon, string tip, Action a)
    {
        var b = new Button
        {
            Content = Icon(icon, IconSize.XS).Themed(TextElement.ForegroundProperty, Muted),
            Width = Sizes.WidgetButton,
            Height = Sizes.WidgetButton,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(Radius.M)
        };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => a();
        return b;
    }

    private static Button LinkBtn(string text, Action a)
    {
        var b = new Button
        {
            Content = Txt(text, TextSize.Small, false, Link),
            Padding = new Thickness(0, Space.XS),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Hand
        };
        b.Click += (_, _) => a();
        return b;
    }

    private Button? _gear;
}

/// <summary>A widget bar: the track, the used part in the accent, and a thin mark where an even pace would be now.</summary>
internal sealed class WidgetBar(double used, double? pace) : Control
{
    public double Used { get; } = used;
    public double? Pace { get; } = pace;

    protected override Size MeasureOverride(Size availableSize) => new(0, Sizes.BarWidget + 2 * Sizes.PaceMarkRise);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, top = Sizes.PaceMarkRise, h = Sizes.BarWidget, r = h / 2;
        context.DrawRectangle(Brush(W.Subtle), null, new Rect(0, top, w, h), r, r);
        var fill = w * Math.Clamp(Used, 0, 100) / 100;
        if (fill > 0)
        {
            context.DrawRectangle(Brush(W.Accent), null, new Rect(0, top, Math.Max(fill, h), h), r, r);
        }

        if (Pace is { } p)
        {
            var x = Math.Clamp(w * p / 100, Sizes.PaceMark / 2, w - Sizes.PaceMark / 2);
            using (context.PushOpacity(PaceMarkOpacity))
            {
                context.DrawRectangle(Brush(W.Text), null, new Rect(x - Sizes.PaceMark / 2, 0, Sizes.PaceMark, Bounds.Height), Sizes.PaceMark / 2, Sizes.PaceMark / 2);
            }
        }
    }

    /// <summary>The pace mark is the text color, faded so it reads as a guide over the bar.</summary>
    public const double PaceMarkOpacity = 0.55;

    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;
}
