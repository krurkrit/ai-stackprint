// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ModelCompareTests
{
    private static ModelRow Row(string key, params (string Field, string Value)[] values) =>
        new(key, "test", values.ToDictionary(v => v.Field, v => v.Value), new Dictionary<string, string>(), new HashSet<string>(), false);

    [Fact]
    public void The_scale_runs_from_the_worst_to_the_best_of_all_models_not_only_the_chosen_ones()
    {
        var best = Row("best", ("contextWindow", "1000000"), ("price.input", "1"));
        var worst = Row("worst", ("contextWindow", "10000"), ("price.input", "100"));
        var mid = Row("mid", ("contextWindow", "100000"), ("price.input", "10"));
        var s = ModelCompare.Score([mid], [best, worst, mid]);
        Assert.Equal(50, s[0].Percent[0], 6);   // 100K is halfway (on a log scale) between 10K and 1M
        Assert.Equal(ModelCompare.PaidBest / 2, s[0].Percent[2], 6);   // $10 is halfway between $1 and $100
        var edges = ModelCompare.Score([best, worst], [best, worst, mid]);
        Assert.Equal(100, edges[0].Percent[0]);
        Assert.Equal(0, edges[1].Percent[0]);
        Assert.Equal(ModelCompare.PaidBest, edges[0].Percent[2]);   // the cheapest paid price: 100 % is kept for free
        Assert.Equal(0, edges[1].Percent[2]);     // the dearest is 0 %
        Assert.Equal(1, edges[0].Raw[2]);
    }

    [Fact]
    public void A_missing_value_is_zero_percent_with_no_raw_value_and_does_not_set_the_scale()
    {
        var a = Row("a", ("maxOutput", "64000"));
        var b = Row("b");
        var s = ModelCompare.Score([a, b], [a, b]);
        Assert.Equal(100, s[0].Percent[1]);   // the only value: nothing to compare with
        Assert.Equal(0, s[1].Percent[1]);
        Assert.Null(s[1].Raw[1]);
        Assert.All(s[0].Percent.Skip(2), p => Assert.Equal(0, p));   // nobody has a price: nothing to compare
    }

    [Fact]
    public void A_free_price_is_100_percent_and_is_not_part_of_the_scale()
    {
        var free = Row("free", ("price.output", "0"));
        var cheap = Row("cheap", ("price.output", "1"));
        var dear = Row("dear", ("price.output", "100"));
        var s = ModelCompare.Score([free, cheap, dear], [free, cheap, dear]);
        Assert.Equal(100, s[0].Percent[3]);
        Assert.Equal(ModelCompare.PaidBest, s[1].Percent[3]);
        Assert.Equal(0, s[2].Percent[3]);
    }
}
