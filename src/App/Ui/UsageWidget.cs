// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The usage widget: a small borderless window with each limit the user chose (docs/mockups/tray-widget.png). Only the card's background is see-through
/// (<see cref="Models.Settings.WidgetOpacity"/>); under the pointer it turns opaque. Dragged anywhere but a button; its place is kept per screen in state.json.
/// </summary>
internal sealed partial class UsageWidget : Window
{
    private readonly AppHost _h;
    private readonly Border _back = new Border { CornerRadius = new CornerRadius(Radius.L) }.Themed(Border.BackgroundProperty, Surface);
    private readonly ContentControl _body = new();
    private readonly DispatcherTimer _save;

    public UsageWidget(AppHost h)
    {
        _h = h;
        Title = "AI Stackprint usage";
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        SizeToContent = SizeToContent.Height;
        Width = Widths.Widget;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = h.S.Settings.WidgetOnTop;
        if (!Motion.Disabled && h.S.Settings.Animations)
        {
            _back.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(Duration.Fast) }];
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(Radius.L),
            BorderThickness = new Thickness(Stroke.Thin),
            Padding = new Thickness(Space.L, Space.M + Space.XXS, Space.L, Space.L),
            Child = new StackPanel { Children = { Header(), _body } }
        }.Themed(Border.BorderBrushProperty, BorderKey);
        Content = new Panel { Children = { _back, card } };
        card.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
        PointerEntered += (_, _) => _back.Opacity = 1;
        PointerExited += (_, _) => _back.Opacity = BackOpacity(h);
        _save = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => SavePlace());
        PositionChanged += (_, _) =>
        {
            _save.Stop();
            _save.Start();
        };
        _clock = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => Update());   // "full about" and the pace marks move with time
        Opened += (_, _) => PutBack();
        Closed += (_, _) =>
        {
            _save.Stop();
            _clock.Stop();
        };
        ActualThemeVariantChanged += (_, _) => Update();
        _clock.Start();
        Update();
    }

    private readonly DispatcherTimer _clock;

    /// <summary>The background opacity's range (Settings: 30 to 100 percent, in steps of 5).</summary>
    public const int OpacityMin = 30, OpacityStep = 5;

    private static double BackOpacity(AppHost h) => Math.Clamp(h.S.Settings.WidgetOpacity, OpacityMin, 100) / 100.0;

    /// <summary>Builds the rows again from the latest readings and applies the settings.</summary>
    public void Update()
    {
        Topmost = _h.S.Settings.WidgetOnTop;
        _back.Opacity = IsPointerOver ? 1 : BackOpacity(_h);
        _body.Content = Rows(_h.WidgetApps());
    }

    /// <summary>The place it was left, inside that screen's work area; the first time, the bottom right, 16 px above the taskbar.</summary>
    private void PutBack()
    {
        var p = _h.S.State.WidgetPosition;
        var screen = p == null ? null : Screens.All.FirstOrDefault(s => s.WorkingArea.X == p.ScreenX && s.WorkingArea.Y == p.ScreenY && s.WorkingArea.Width == p.ScreenWidth && s.WorkingArea.Height == p.ScreenHeight);
        if (p == null || screen == null)
        {
            TrayNotice.Place(this, Space.XL);
            return;
        }

        var a = screen.WorkingArea;
        var (w, hgt) = ((int)Math.Ceiling(Bounds.Width * screen.Scaling), (int)Math.Ceiling(Bounds.Height * screen.Scaling));
        Position = new PixelPoint(Math.Clamp(p.X, a.X, Math.Max(a.X, a.Right - w)), Math.Clamp(p.Y, a.Y, Math.Max(a.Y, a.Bottom - hgt)));
    }

    private void SavePlace()
    {
        _save.Stop();
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen)
        {
            return;
        }

        var a = screen.WorkingArea;
        _h.S.State.WidgetPosition = new Models.WidgetPlace { X = Position.X, Y = Position.Y, ScreenX = a.X, ScreenY = a.Y, ScreenWidth = a.Width, ScreenHeight = a.Height };
        Services.StateStore.Save(_h.S.State);
    }
}
