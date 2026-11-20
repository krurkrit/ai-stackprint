// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using FluentAvalonia.UI.Controls;

using Xunit;

namespace AiStackprint.App.Tests;

public class DatePickerTests
{
    [AvaloniaFact]
    public void The_calendar_icon_of_a_date_picker_is_a_card_header_size_icon()
    {
        var picker = new CalendarDatePicker
        {
            SelectedDate = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Local),
            Width = 220
        };
        Ui.Show(picker);
        var glyph = picker.GetVisualDescendants().OfType<FAFontIcon>().Single();
        Assert.Equal(IconSize.M, glyph.FontSize);
        Assert.Equal(IconSize.M, glyph.Bounds.Height);
    }
}
