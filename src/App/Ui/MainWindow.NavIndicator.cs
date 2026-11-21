// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The side menu's selection: one background that slides from the item you were on to the item you just clicked (it is not painted by each item).</summary>
internal partial class MainWindow
{
    private const int SlideMs = 260;
    private readonly Border _indicator = new Border
    {
        Name = "NavIndicator",
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        CornerRadius = new CornerRadius(Radius.L),
        IsHitTestVisible = false,
        IsVisible = false
    }.Themed(Border.BackgroundProperty, Surface);

    private Button? _activeBtn;      // the item of the page that is open, found while the menu is built
    private string _navKind = "";    // main or settings: the other menu's items are elsewhere, so the background jumps instead of sliding
    private bool _indicatorPlaced;   // the background has a place (the first time it appears, it does not slide in)

    private static Transitions Slide() =>
    [
        new ThicknessTransition { Property = Layoutable.MarginProperty, Duration = TimeSpan.FromMilliseconds(SlideMs), Easing = new CubicEaseOut() },
        new DoubleTransition { Property = Layoutable.HeightProperty, Duration = TimeSpan.FromMilliseconds(SlideMs), Easing = new CubicEaseOut() }
    ];

    /// <summary>The menu with the sliding background behind it. Call after the items are made (<see cref="NavBtn"/> notes the open page's item).</summary>
    private Control WithIndicator(Control menu, string kind)
    {
        var grid = new Grid();
        grid.Children.Add(_indicator.Free());
        grid.Children.Add(menu);
        var jump = _navKind != kind || !_indicatorPlaced;
        _navKind = kind;
        var target = _activeBtn;
        Dispatcher.UIThread.Post(() => Place(grid, target, jump), DispatcherPriority.Loaded);   // after layout: the item's place is known
        return grid;
    }

    private void Place(Grid grid, Button? target, bool jump)
    {
        if (target == null || !target.IsLoaded || target.TranslatePoint(default, grid) is not { } at)
        {
            _indicator.IsVisible = false;
            _indicatorPlaced = false;
            return;
        }

        if (jump)
        {
            _indicator.Transitions = null;
        }
        else
        {
            _indicator.Transitions ??= Slide();
        }

        _indicator.Margin = new Thickness(0, at.Y, 0, 0);
        _indicator.Height = target.Bounds.Height;
        _indicator.IsVisible = true;
        _indicatorPlaced = true;
        if (jump)
        {
            Dispatcher.UIThread.Post(() => _indicator.Transitions = Slide(), DispatcherPriority.Background);   // the next move slides
        }
    }
}
