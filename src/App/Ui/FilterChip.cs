// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>A chip that shows or hides something: on it looks like any chip, off it is dimmed. A click switches it.</summary>
internal sealed class FilterChip : Border
{
    private bool _on = true;
    private readonly Action<bool> _changed;

    public FilterChip(Control content, string tip, Action<bool> changed)
    {
        _changed = changed;
        Child = content;
        CornerRadius = new CornerRadius(Radius.Pill);
        Padding = new Thickness(Space.L, Space.XS);
        Margin = new Thickness(0, 0, Space.M, Space.S);
        HorizontalAlignment = HorizontalAlignment.Left;
        Cursor = Hand;
        this.Themed(BackgroundProperty, Subtle);
        ToolTip.SetTip(this, tip);
        PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left)
            {
                On = !On;
            }
        };
    }

    /// <summary>Shown (true, the start) or hidden.</summary>
    public bool On
    {
        get => _on;
        set
        {
            if (_on == value)
            {
                return;
            }

            _on = value;
            Opacity = value ? 1 : Dimmed;
            _changed(value);
        }
    }

    private const double Dimmed = .45;
}
