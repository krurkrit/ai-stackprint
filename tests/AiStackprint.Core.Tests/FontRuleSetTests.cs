// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

#pragma warning disable S3881 // a test class that removes its temp folder
public partial class FontRuleSetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fontset-" + Guid.NewGuid().ToString("N"));

    public FontRuleSetTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private static FontNumberRule Own(double value, FontLimit? limit = null) => new()
    {
        Value = value,
        Limit = limit
    };

    private static FontNumberRule Adjust(double value, FontLimit? limit = null) => new()
    {
        Mode = FontMode.Adjust,
        Value = value,
        Limit = limit
    };

    private static FontNumberRule Link(FontLimit? limit = null) => new()
    {
        Mode = FontMode.Link,
        Limit = limit
    };

    /// <summary>The example of the spec: a body of 14 px, weight 400 and a header that links its family and adjusts size (+2) and weight (+200, options 400, 700, 900).</summary>
    private static Dictionary<string, FontRule> Sample() => new()
    {
        ["body"] = new()
        {
            Family = new() { Value = "Inter" },
            Size = Own(14, new()
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
        },
        ["header"] = new()
        {
            Parent = "body",
            Family = new() { Mode = FontMode.Link },
            Size = Adjust(2),
            Weight = Adjust(200, new() { Options = [400, 700, 900] })
        }
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
        rules["body"].Weight = Own(500, new()
        {
            Min = 100,
            Max = 900,
            Step = 100
        });
        rules["header"].Weight = Adjust(100, new() { Options = [400, 800] });    // 600: a tie of 400 and 800, up
        Assert.Equal(800, new FontRuleSet(rules).Resolve("header").Weight);
        rules["header"].Weight = Adjust(-100, new() { Options = [200, 600] });   // 400: a tie of 200 and 600, down
        Assert.Equal(200, new FontRuleSet(rules).Resolve("header").Weight);
        rules["body"].Weight = Own(600, new()
        {
            Min = 200,
            Max = 800,
            Step = 200
        });
        rules["header"].Weight = Adjust(200, new() { Options = [400, 700, 900] });   // 800: a tie of 700 and 900, up
        Assert.Equal(900, new FontRuleSet(rules).Resolve("header").Weight);
    }

    [Fact]
    public void A_size_is_clamped_then_snapped_to_the_step_that_starts_at_min()
    {
        var rules = Sample();
        rules["body"].Size = Own(15, new()
        {
            Min = 10,
            Max = 24,
            Step = 2
        });
        var set = new FontRuleSet(rules);
        Assert.Equal(16d, set.Resolve("body").Size);   // 10, 12, 14, 16: 15 is halfway, up
        Assert.Equal(24d, set.FitSize("body", 99));
        Assert.Equal(10d, set.FitSize("body", -5));
    }

    [Fact]
    public void A_linked_value_is_fitted_to_the_limits_of_the_one_that_links()
    {
        var rules = Sample();
        rules["body"].Size = Own(30, new()
        {
            Min = 10,
            Max = 40,
            Step = 1
        });
        rules["header"].Size = Link(new()
        {
            Min = 10,
            Max = 24,
            Step = 1
        });
        Assert.Equal(24d, new FontRuleSet(rules).Resolve("header").Size);
    }

    [Fact]
    public void Without_a_value_a_setting_is_Inter_14_400_fitted_to_its_limits()
    {
        var rules = new Dictionary<string, FontRule>
        {
            ["x"] = new()
            {
                Size = new()
                {
                    Limit = new()
                    {
                        Min = 16,
                        Max = 20,
                        Step = 1
                    }
                },
                Weight = new()
                {
                    Limit = new()
                    {
                        Min = 500,
                        Max = 700,
                        Step = 100
                    }
                }
            }
        };
        var r = new FontRuleSet(rules).Resolve("x");
        Assert.Equal(("Inter", 16d, 500), (r.Family, r.Size, r.Weight));
    }

    [Fact]
    public void A_parent_that_would_make_a_loop_is_refused_and_not_offered()
    {
        var rules = Sample();
        rules["third"] = new()
        {
            Parent = "header",
            Family = new() { Mode = FontMode.Link },
            Size = Link(),
            Weight = Link()
        };
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
}
