// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using AiStackprint.Models;
using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Core.Tests;

#pragma warning disable S3881 // a test class that removes its temp folder
public partial class FontRuleSetTests
{
    [Fact]
    public void Switching_mode_keeps_the_look()
    {
        var set = new FontRuleSet(Sample());
        set.SetMode("header", FontProp.Size, FontMode.Set);
        Assert.Equal(16d, set.Number("header", FontProp.Size));
        set.SetNumber("header", FontProp.Size, 20);
        set.SetMode("header", FontProp.Size, FontMode.Adjust);
        Assert.Equal(6d, set.Number("header", FontProp.Size));   // 20 - 14
        set.SetMode("header", FontProp.Weight, FontMode.Link);
        Assert.Equal(400, set.Resolve("header").Weight);   // the parent's 400, which is one of the header's options
        Assert.Throws<InvalidOperationException>(() => set.SetMode("body", FontProp.Size, FontMode.Link));
        Assert.Throws<InvalidOperationException>(() => set.SetMode("header", FontProp.Family, FontMode.Adjust));
    }

    [Fact]
    public void Changing_the_parent_keeps_an_adjusted_field_where_it_looks()
    {
        var rules = Sample();
        rules["other"] = new()
        {
            Family = new() { Value = "Roboto" },
            Size = Own(20, new()
            {
                Min = 10,
                Max = 24,
                Step = 1
            }),
            Weight = Own(400, new()
            {
                Min = 200,
                Max = 600,
                Step = 200
            })
        };
        var set = new FontRuleSet(rules);
        set.SetParent("header", "other");
        Assert.Equal(16d, set.Resolve("header").Size);
        Assert.Equal(-4d, set.Number("header", FontProp.Size));
    }

    [Fact]
    public void An_adjusted_field_explains_itself()
    {
        var set = new FontRuleSet(Sample());
        Assert.Equal("Body Normal (400) +200 = 600 → nearest allowed Bold (700)", set.Describe("header", FontProp.Weight));
        Assert.Equal("", set.Describe("body", FontProp.Weight));
        Assert.Equal("", set.Describe("header", FontProp.Family));
    }

    [Fact]
    public void A_linked_field_ignores_new_values_and_an_adjusted_one_is_kept_within_a_thousand()
    {
        var set = new FontRuleSet(Sample());
        set.SetMode("header", FontProp.Weight, FontMode.Set);
        set.SetMode("header", FontProp.Weight, FontMode.Adjust);
        Assert.Equal(300d, set.Number("header", FontProp.Weight));   // 700 - 400: the look is kept
        set.SetNumber("header", FontProp.Weight, 5000);
        Assert.Equal(1000d, set.Number("header", FontProp.Weight));
        set.SetMode("header", FontProp.Size, FontMode.Link);
        set.SetNumber("header", FontProp.Size, 30);
        Assert.Equal(14d, set.Resolve("header").Size);
        set.SetNumber("header", FontProp.Family, 1);   // not a number: nothing

        set.SetMode("header", FontProp.Family, FontMode.Link);
        set.SetName("header", "Roboto");
        Assert.Equal(set.Resolve("body").Family, set.Resolve("header").Family);
        set.SetMode("header", FontProp.Family, FontMode.Set);
        set.SetName("header", "Roboto");
        Assert.Equal("Roboto", set.Resolve("header").Family);
    }

    [Fact]
    public void Wrong_sizes_and_limits_are_named()
    {
        static Dictionary<string, FontRule> With(Action<Dictionary<string, FontRule>> change)
        {
            var r = Sample();
            change(r);
            return r;
        }

        Assert.Contains("size", FontRuleSet.Validate(With(r => r["body"].Size = Own(0))));
        Assert.Contains("options must be", FontRuleSet.Validate(With(r => r["body"].Weight = Own(400, new() { Options = [double.NaN] }))));
        Assert.Contains("limit is not valid", FontRuleSet.Validate(With(r => r["body"].Size = Own(14, new() { Step = 0 }))));
        Assert.Contains("limit is not valid", FontRuleSet.Validate(With(r => r["body"].Size = Own(14, new() { Min = 20, Max = 10 }))));
        Assert.Contains("limit is not valid", FontRuleSet.Validate(With(r => r["body"].Size = Own(14, new() { Min = -1 }))));
    }

    [Theory]
    [InlineData(100, "Thin (100)")]
    [InlineData(200, "ExtraLight (200)")]
    [InlineData(300, "Light (300)")]
    [InlineData(400, "Normal (400)")]
    [InlineData(500, "Medium (500)")]
    [InlineData(600, "SemiBold (600)")]
    [InlineData(700, "Bold (700)")]
    [InlineData(800, "ExtraBold (800)")]
    [InlineData(900, "Black (900)")]
    [InlineData(450.4, "Weight (450)")]
    public void Weights_have_their_names(double weight, string label) => Assert.Equal(label, FontWeights.Label(weight));

    [Fact]
    public void Wrong_settings_are_named()
    {
        static Dictionary<string, FontRule> With(Action<Dictionary<string, FontRule>> change)
        {
            var r = Sample();
            change(r);
            return r;
        }

        Assert.Null(FontRuleSet.Validate(Sample()));
        Assert.Contains("unknown parent", FontRuleSet.Validate(With(r => r["header"].Parent = "nope")));
        Assert.Contains("without a parent", FontRuleSet.Validate(With(r => r["body"].Size = Link())));
        Assert.Contains("Cycle at", FontRuleSet.Validate(With(r => r["body"].Parent = "header")));
        Assert.Contains("weight", FontRuleSet.Validate(With(r => r["body"].Weight = Own(0))));   // 0 is not valid CSS
        Assert.Contains("weight", FontRuleSet.Validate(With(r => r["body"].Weight = Own(400, new() { Options = [400, 1200] }))));
        Assert.Contains("adjusted", FontRuleSet.Validate(With(r => r["header"].Family = new() { Mode = FontMode.Adjust })));
    }

    [Fact]
    public void The_built_in_defaults_are_valid_and_give_todays_look()
    {
        var d = SettingsStore.Defaults();
        Assert.Null(FontRuleSet.Validate(d.Fonts));
        Assert.Equal(FontRoles.All.Select(r => r.Key).Order(), d.Fonts.Keys.Order());
        Assert.Equal(("Inter", 14d, 400), (d.Font().Family, d.Font().Size, d.Font().Weight));
        Assert.Equal(600, d.Font(FontRoles.Emphasis).Weight);
        Assert.Equal(("Inter", 700), (d.Font(FontRoles.Heading).Family, d.Font(FontRoles.Heading).Weight));
        Assert.Equal(500, d.Font(FontRoles.Menu).Weight);
        Assert.Equal(("JetBrains Mono", 14d, 400), (d.Font(FontRoles.Log).Family, d.Font(FontRoles.Log).Size, d.Font(FontRoles.Log).Weight));
    }

    [Fact]
    public void The_flat_font_keys_of_earlier_builds_are_dropped_and_never_written_back()
    {
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, """{ "version": 1, "theme": "Dark", "fontFamily": "Roboto", "fontSize": 16, "emphasisWeight": 500, "menuWeight": 600 }""");
        var s = SettingsStore.Load(file);
        Assert.Equal(("Inter", 14d, 600), (s.Font().Family, s.Font().Size, s.Font(FontRoles.Emphasis).Weight));   // the defaults: 0.x settings are not carried over
        Assert.Null(s.Extra);

        SettingsStore.Save(s, file);
        Assert.Equal("""{"version":1,"theme":"Dark"}""", JsonNode.Parse(File.ReadAllText(file))!.ToJsonString());
    }

    [Fact]
    public void A_changed_mode_is_written_so_a_default_link_can_be_turned_into_an_own_value()
    {
        var file = Path.Combine(_dir, "settings.json");
        var s = SettingsStore.Defaults();
        new FontRuleSet(s.Fonts).SetMode(FontRoles.Emphasis, FontProp.Weight, FontMode.Set);
        SettingsStore.Save(s, file);
        var weight = JsonNode.Parse(File.ReadAllText(file))!["fonts"]!["emphasis"]!["weight"]!;
        Assert.Equal("""{"mode":"set","value":600}""", weight.ToJsonString());
        Assert.Equal(FontMode.Set, new FontRuleSet(SettingsStore.Load(file).Fonts).Mode(FontRoles.Emphasis, FontProp.Weight));
    }

    [Fact]
    public void A_field_without_a_mode_is_its_own_value_with_no_parent_and_linked_with_one()
    {
        var d = SettingsStore.Defaults();
        var set = new FontRuleSet(d.Fonts);
        Assert.Null(d.Fonts[FontRoles.Emphasis].Size.Mode);
        Assert.Equal(FontMode.Link, set.Mode(FontRoles.Emphasis, FontProp.Size));
        Assert.Equal(FontMode.Set, set.Mode(FontRoles.Body, FontProp.Size));
        Assert.Equal(FontMode.Adjust, set.Mode(FontRoles.Emphasis, FontProp.Weight));
        set.SetParent(FontRoles.Body, FontRoles.Log);   // the fields it had as own values stay so: a new parent changes nothing about how Body looks
        Assert.Equal(FontMode.Set, set.Mode(FontRoles.Body, FontProp.Size));
        Assert.Equal(14d, set.Resolve(FontRoles.Body).Size);
    }

    [Fact]
    public void The_file_holds_no_mode_that_the_parent_already_implies()
    {
        var file = Path.Combine(_dir, "settings.json");
        var s = SettingsStore.Defaults();
        var set = new FontRuleSet(s.Fonts);
        set.SetParent(FontRoles.Heading, "");                                    // own values now; a default parent turned off
        set.SetMode(FontRoles.Emphasis, FontProp.Weight, FontMode.Link);        // the default is "adjust": a different mode is written
        set.SetMode(FontRoles.Menu, FontProp.Size, FontMode.Set);               // a linked field made its own: written
        SettingsStore.Save(s, file);
        var fonts = JsonNode.Parse(File.ReadAllText(file))!["fonts"]!;
        Assert.Equal("""{"parent":"","family":{"value":"Inter"},"size":{"value":14},"weight":{"mode":"set","value":700}}""", fonts["heading"]!.ToJsonString());
        Assert.Equal("""{"weight":{"mode":"link"}}""", fonts["emphasis"]!.ToJsonString());
        Assert.Equal("""{"size":{"mode":"set","value":14}}""", fonts["menu"]!.ToJsonString());
        var back = SettingsStore.Load(file);
        var again = new FontRuleSet(back.Fonts);
        Assert.Equal(("Inter", 14d, 700), (back.Font(FontRoles.Heading).Family, back.Font(FontRoles.Heading).Size, back.Font(FontRoles.Heading).Weight));
        Assert.Equal(FontMode.Link, again.Mode(FontRoles.Emphasis, FontProp.Weight));
        Assert.Equal(400, back.Font(FontRoles.Emphasis).Weight);
        Assert.Equal(FontMode.Set, again.Mode(FontRoles.Menu, FontProp.Size));
    }

    [Fact]
    public void A_file_with_a_loop_or_a_bad_parent_falls_back_to_the_default_fonts()
    {
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, """{ "version": 1, "theme": "Dark", "fonts": { "body": { "parent": "heading", "size": { "mode": "link" } } } }""");
        var s = SettingsStore.Load(file);
        Assert.Equal("Dark", s.Theme);
        Assert.Null(FontRuleSet.Validate(s.Fonts));
        Assert.Equal(14d, s.Font().Size);
    }

    [Fact]
    public void Only_a_changed_font_is_saved_and_a_changed_parent_survives_a_reload()
    {
        var file = Path.Combine(_dir, "settings.json");
        var s = SettingsStore.Defaults();
        new FontRuleSet(s.Fonts).SetParent(FontRoles.Heading, "");   // a default parent turned off must stay off
        SettingsStore.Save(s, file);
        var saved = JsonNode.Parse(File.ReadAllText(file))!.AsObject()["fonts"]!.AsObject();
        Assert.Equal(["heading"], saved.Select(k => k.Key));
        var back = SettingsStore.Load(file);
        Assert.Equal("", new FontRuleSet(back.Fonts).Parent(FontRoles.Heading));
        Assert.Equal(700, back.Font(FontRoles.Heading).Weight);
    }
}
