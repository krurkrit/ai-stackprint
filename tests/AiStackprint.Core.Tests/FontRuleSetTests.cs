// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

#pragma warning disable S3881 // a test class that removes its temp folder
public class FontRuleSetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fontset-" + Guid.NewGuid().ToString("N"));

    public FontRuleSetTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private static FontNumberRule Own(double value, FontLimit? limit = null) => new() { Value = value, Limit = limit };

    private static FontNumberRule Adjust(double value, FontLimit? limit = null) => new() { Mode = FontMode.Adjust, Value = value, Limit = limit };

    private static FontNumberRule Link(FontLimit? limit = null) => new() { Mode = FontMode.Link, Limit = limit };

    /// <summary>The example of the spec: a body of 14 px, weight 400 and a header that links its family and adjusts size (+2) and weight (+200, options 400, 700, 900).</summary>
    private static Dictionary<string, FontRule> Sample() => new()
    {
        ["body"] = new() { Family = new() { Value = "Inter" }, Size = Own(14, new() { Min = 10, Max = 24, Step = 1 }), Weight = Own(400, new() { Min = 200, Max = 600, Step = 200 }) },
        ["header"] = new() { Parent = "body", Family = new() { Mode = FontMode.Link }, Size = Adjust(2), Weight = Adjust(200, new() { Options = [400, 700, 900] }) }
    };

    [Fact]
    public void A_header_follows_its_parent_with_an_offset_fitted_to_its_own_weights()
    {
        var r = new FontRuleSet(Sample()).Resolve("header");
        Assert.Equal(("Inter", 16d, 700), (r.Family, r.Size, r.Weight));   // 400 + 200 = 600: the nearest of 400, 700, 900 is 700
        Assert.Equal(600d, r.RawWeight);
    }

    [Fact]
    public void A_tie_goes_the_way_of_the_offset()
    {
        var rules = Sample();
        rules["body"].Weight = Own(500, new() { Min = 100, Max = 900, Step = 100 });
        rules["header"].Weight = Adjust(100, new() { Options = [400, 800] });    // 600: a tie of 400 and 800, up
        Assert.Equal(800, new FontRuleSet(rules).Resolve("header").Weight);
        rules["header"].Weight = Adjust(-100, new() { Options = [200, 600] });   // 400: a tie of 200 and 600, down
        Assert.Equal(200, new FontRuleSet(rules).Resolve("header").Weight);
        rules["body"].Weight = Own(600, new() { Min = 200, Max = 800, Step = 200 });
        rules["header"].Weight = Adjust(200, new() { Options = [400, 700, 900] });   // 800: a tie of 700 and 900, up
        Assert.Equal(900, new FontRuleSet(rules).Resolve("header").Weight);
    }

    [Fact]
    public void A_size_is_clamped_then_snapped_to_the_step_that_starts_at_min()
    {
        var rules = Sample();
        rules["body"].Size = Own(15, new() { Min = 10, Max = 24, Step = 2 });
        var set = new FontRuleSet(rules);
        Assert.Equal(16d, set.Resolve("body").Size);   // 10, 12, 14, 16: 15 is halfway, up
        Assert.Equal(24d, set.FitSize("body", 99));
        Assert.Equal(10d, set.FitSize("body", -5));
    }

    [Fact]
    public void A_linked_value_is_fitted_to_the_limits_of_the_one_that_links()
    {
        var rules = Sample();
        rules["body"].Size = Own(30, new() { Min = 10, Max = 40, Step = 1 });
        rules["header"].Size = Link(new() { Min = 10, Max = 24, Step = 1 });
        Assert.Equal(24d, new FontRuleSet(rules).Resolve("header").Size);
    }

    [Fact]
    public void Without_a_value_a_setting_is_Inter_14_400_fitted_to_its_limits()
    {
        var rules = new Dictionary<string, FontRule> { ["x"] = new() { Size = new() { Limit = new() { Min = 16, Max = 20, Step = 1 } }, Weight = new() { Limit = new() { Min = 500, Max = 700, Step = 100 } } } };
        var r = new FontRuleSet(rules).Resolve("x");
        Assert.Equal(("Inter", 16d, 500), (r.Family, r.Size, r.Weight));
    }

    [Fact]
    public void A_parent_that_would_make_a_loop_is_refused_and_not_offered()
    {
        var rules = Sample();
        rules["third"] = new() { Parent = "header", Family = new() { Mode = FontMode.Link }, Size = Link(), Weight = Link() };
        var set = new FontRuleSet(rules);
        Assert.True(set.WouldCycle("body", "third"));
        Assert.True(set.WouldCycle("body", "body"));
        Assert.False(set.WouldCycle("third", "body"));
        Assert.DoesNotContain("third", set.ParentChoices("body"));
        Assert.DoesNotContain("third", set.ParentChoices("header"));
        Assert.Throws<InvalidOperationException>(() => set.SetParent("body", "third"));
        Assert.Throws<ArgumentException>(() => set.SetParent("body", "nobody"));
        Assert.Equal("", set.Parent("body"));
    }

    [Fact]
    public void Resolving_a_loop_in_the_data_ends()
    {
        var rules = Sample();
        rules["body"].Parent = "header";
        rules["body"].Size = Link();
        Assert.NotNull(FontRuleSet.Validate(rules));
        _ = new FontRuleSet(rules).Resolve("header");   // returns: nothing recurses forever
    }

    [Fact]
    public void Taking_the_parent_away_turns_every_field_into_its_current_value()
    {
        var set = new FontRuleSet(Sample());
        set.SetParent("header", "");
        var r = set.Resolve("header");
        Assert.Equal(("Inter", 16d, 700), (r.Family, r.Size, r.Weight));
        Assert.All(new[] { FontProp.Family, FontProp.Size, FontProp.Weight }, p => Assert.Equal(FontMode.Set, set.Mode("header", p)));
        Assert.Null(FontRuleSet.Validate(set.Rules));
    }

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
        rules["other"] = new() { Family = new() { Value = "Roboto" }, Size = Own(20, new() { Min = 10, Max = 24, Step = 1 }), Weight = Own(400, new() { Min = 200, Max = 600, Step = 200 }) };
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
