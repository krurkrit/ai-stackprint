// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>The font setting control: one row with a parent, and a mode button in front of each field.</summary>
public class FontSettingTests
{
    private static (FontRuleSet Set, FontSetting Control, List<FontSetting> Calls) Make(string key)
    {
        var set = new FontRuleSet(SettingsStore.Defaults().Fonts);
        var calls = new List<FontSetting>();
        var control = new FontSetting(set, key, ["Inter", "Roboto"], calls.Add);
        Ui.Show(control.View);
        return (set, control, calls);
    }

    private static List<T> All<T>(FontSetting c) where T : Control => [.. c.View.GetVisualDescendants().OfType<T>()];

    private static Button ModeButton(FontSetting c, string tip) => All<Button>(c).Single(b => ToolTip.GetTip(b) as string == tip);

    [AvaloniaFact]
    public void A_setting_with_no_parent_shows_own_values_and_disabled_mode_buttons()
    {
        var (_, c, _) = Make(FontRoles.Body);
        var modes = All<Button>(c).Where(b => ToolTip.GetTip(b) as string == "Set").ToList();
        Assert.Equal(3, modes.Count);   // family, size, weight
        Assert.All(modes, b => Assert.False(b.IsEnabled));
        Assert.Equal(14m, All<NumericUpDown>(c).Single().Value);
        var combos = All<ComboBox>(c);
        Assert.Equal(["None", "Code & logs"], combos[0].ItemsSource!.Cast<string>());   // the others lead back to Body: a loop
        Assert.Equal("Inter", (string)combos[1].SelectedItem!);
        Assert.Equal("Normal (400)", (string)combos[2].SelectedItem!);
    }

    [AvaloniaFact]
    public void An_adjusted_weight_shows_its_offset_and_the_mode_button_goes_on_to_own_value()
    {
        var (set, c, calls) = Make(FontRoles.Emphasis);
        Assert.Equal(4, All<ComboBox>(c).Count + All<NumericUpDown>(c).Count);   // parent, family, size and weight: every role shows all four
        var offset = All<NumericUpDown>(c).Single(n => n.IsEnabled);
        Assert.Equal(200m, offset.Value);
        Assert.Equal("Body Normal (400) +200 = 600", ToolTip.GetTip(offset));

        Ui.Click(ModeButton(c, "Adjust Body"));
        Assert.Equal(FontMode.Set, set.Mode(FontRoles.Emphasis, FontProp.Weight));
        Assert.Equal(600d, set.Number(FontRoles.Emphasis, FontProp.Weight));   // the look stays
        Assert.Single(calls);
        Assert.Equal("SemiBold (600)", (string)All<ComboBox>(c)[2].SelectedItem!);
    }

    [AvaloniaFact]
    public void A_linked_field_shows_the_parents_value_and_cannot_be_edited()
    {
        var (_, c, _) = Make(FontRoles.Heading);
        var family = All<ComboBox>(c)[1];
        Assert.False(family.IsEnabled);
        Assert.Contains("Inter", family.SelectedItem!.ToString());
        Assert.Equal(2, All<Button>(c).Count(b => ToolTip.GetTip(b) as string == "Use Emphasis"));   // family and size follow it; the weight adds 100
        Assert.NotNull(ModeButton(c, "Adjust Emphasis"));
    }

    [AvaloniaFact]
    public void Choosing_no_parent_makes_every_field_its_own_value()
    {
        var (set, c, calls) = Make(FontRoles.Heading);
        All<ComboBox>(c)[0].SelectedItem = "None";
        Assert.Equal("", set.Parent(FontRoles.Heading));
        Assert.All(new[] { FontProp.Family, FontProp.Weight }, p => Assert.Equal(FontMode.Set, set.Mode(FontRoles.Heading, p)));
        Assert.Equal(700, set.Resolve(FontRoles.Heading).Weight);   // the same look
        Assert.NotEmpty(calls);
        Assert.All(All<Button>(c).Where(b => ToolTip.GetTip(b) as string == "Set"), b => Assert.False(b.IsEnabled));
    }

    [AvaloniaFact]
    public void Typing_a_size_is_fitted_to_the_limits_and_the_page_is_told()
    {
        var (set, c, calls) = Make(FontRoles.Body);
        var size = All<NumericUpDown>(c).Single();
        size.Value = 40;
        Assert.Equal(18d, set.Resolve(FontRoles.Body).Size);   // Body goes to 18 at most
        Assert.Equal(18m, size.Value);
        Assert.NotEmpty(calls);
    }
}
