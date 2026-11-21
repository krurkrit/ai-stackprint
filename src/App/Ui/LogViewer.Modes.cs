// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AiStackprint.Ui;

/// <summary>The view switch: Raw (the file as written), Conversation (cards) and Compact (one line per step); a text log has Raw and Lines.</summary>
internal partial class LogViewer
{
    private bool IsCompact => ShowReadable && Compact && !IsLines;

    private Control ModeSwitch(Action rebuild)
    {
        var group = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = Space.XXS };
        void Add(string text, string tip, bool on, Action set)
        {
            var b = new ToggleButton
            {
                Content = text,
                IsChecked = on,
                Padding = new Thickness(Space.L, Space.XS),
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            ToolTip.SetTip(b, tip);
            b.IsCheckedChanged += (_, _) =>
            {
                if (b.IsChecked != true || on)
                {
                    b.IsChecked = on;   // the view in use stays selected
                    return;
                }

                RememberPlace();
                set();
                rebuild();
            };
            group.Children.Add(b);
        }

        Add("Raw", "Show the file as written", !ShowReadable, () => Readable = false);
        if (IsLines)
        {
            Add("Lines", "Show one row per log line, with level colors", ShowReadable, () => Readable = true);
        }
        else
        {
            Add("Conversation", "Show the session as cards", ShowReadable && !Compact, () =>
            {
                Readable = true;
                Compact = false;
            });
            Add("Compact", "One line per step; click a line for the full step", IsCompact, () =>
            {
                Readable = true;
                Compact = true;
            });
        }

        return group;
    }
}
