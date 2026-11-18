// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AiStackprint.Ui;

/// <summary>
/// The text of the app: it can be selected by dragging and copied (Ctrl+C, or right-click › Copy in the main window). Inside something that is clicked, a button or a list row,
/// a drag would fight the click, so there it does not select (the click goes through as before) and right-click › Copy still copies the whole text.
/// </summary>
internal sealed class CopyableText : SelectableTextBlock
{
    private bool _inClickTarget;

    /// <summary>True when the text sits in a button or a clickable row: no selecting by drag.</summary>
    public bool InClickTarget => _inClickTarget;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inClickTarget = Equals(Cursor, W.Hand) || this.GetVisualAncestors().Any(a => a is Button or ToggleButton or MenuItem or ComboBoxItem or ListBoxItem || (a is InputElement { Cursor: not null } i && Equals(i.Cursor, W.Hand)));
        if (_inClickTarget)
        {
            Cursor = null;   // the hand of the row, not the text cursor
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!_inClickTarget)   // in a click target the press is not for selecting: it reaches the button or row
        {
            base.OnPointerPressed(e);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_inClickTarget)
        {
            base.OnPointerMoved(e);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_inClickTarget)
        {
            base.OnPointerReleased(e);
        }
    }
}
