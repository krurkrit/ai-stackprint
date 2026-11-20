// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text.Json.Nodes;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class BenchmarkTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bench-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private static Benchmark B(string slug, double? intel = null, double? speed = null, double? ttft = null, double? cost = null, string? name = null) =>
        new(slug, name ?? slug, "Test", intel, null, null, null, null, speed, ttft, cost);

    [Fact]
    public void A_model_is_matched_by_slug_with_effort_or_date_words_and_never_by_a_longer_model()
    {
        string[] keys = ["gpt-5", "gpt-5-mini", "claude-sonnet-5-5"];
        var all = new[]
        {
            B("gpt-5-high", 70), B("gpt-5-medium", 60), B("gpt-5", 50), B("gpt-5-mini-high", 40), B("gpt-5-mini", 30), B("gpt-5-pro", 90),
            B("claude-sonnet-5-5-medium", 31), B("claude-sonnet-5-5-high-20260928", 33), B("claude-sonnet-5-5-opus-like", 99),
        };
        Assert.Equal(["gpt-5-high", "gpt-5-medium", "gpt-5"], BenchmarkStore.Variants("gpt-5", keys, all).Select(b => b.Slug));
        Assert.Equal(["gpt-5-mini-high", "gpt-5-mini"], BenchmarkStore.Variants("gpt-5-mini", keys, all).Select(b => b.Slug));
        Assert.Equal("gpt-5-high", BenchmarkStore.Best("gpt-5", keys, all)!.Slug);   // the highest intelligence of the runs
        Assert.Equal("claude-sonnet-5-5-high-20260928", BenchmarkStore.Best("claude-sonnet-5-5", keys, all)!.Slug);
        Assert.Null(BenchmarkStore.Best("gemini-9", keys, all));
    }

    [Fact]
    public void Dots_and_dashes_in_a_model_key_match_the_same_slug()
    {
        Assert.Equal("gpt-4-1", BenchmarkStore.Best("gpt-4.1", ["gpt-4.1"], [B("gpt-4-1", 20)])!.Slug);
    }

    [Fact]
    public void Scores_run_from_the_worst_to_the_best_of_all_rows_and_lower_is_better_for_first_token_and_cost()
    {
        var all = new[] { B("best", 80, 400, 0.1, 0.01), B("worst", 20, 10, 10, 10), B("mid", 50, 63.2455532, 1, 0.316227766) };
        var r = BenchmarkCompare.Score(["mid", "best", "none"], ["mid", "best", "worst", "none"], all);
        Assert.Equal(["none"], r.Missing);
        Assert.Equal(["mid", "best"], r.Scores.Select(s => s.Key));
        var mid = r.Scores[0].Percent;
        Assert.Equal(50, mid[0], 6);   // intelligence: linear, 50 is halfway between 20 and 80
        Assert.Equal(50, mid[5], 6);   // speed: log, halfway between 10 and 400 (63.2 is the geometric middle)
        Assert.Equal(50, mid[6], 6);   // first token: log, 1 s is halfway between 0.1 and 10; lower is better
        Assert.Equal(50, mid[7], 6);   // cost per task
        var best = r.Scores[1].Percent;
        Assert.Equal([100, 0, 0, 0, 0, 100, 100, 100], best.Select(p => Math.Round(p)));   // no profession scores: 0, shown as unknown
        Assert.Null(r.Scores[1].Raw[1]);
    }

    [Fact]
    public void Rows_are_saved_and_read_back_with_missing_values_kept_missing()
    {
        var file = Path.Combine(_dir, "benchmarks.json");
        BenchmarkStore.Save([B("a", 10.5, 99, null, 0.25, "A (High)"), B("b")], file);
        var back = BenchmarkStore.Load(file);
        Assert.Equal(2, back.Count);
        Assert.Equal(("A (High)", 10.5, 99.0, null, 0.25), (back[0].Name, back[0].Intelligence, back[0].Speed, back[0].FirstToken, back[0].CostPerTask));
        Assert.Null(back[1].Intelligence);
        Assert.Empty(BenchmarkStore.Load(Path.Combine(_dir, "missing.json")));
    }

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body) };

    private static string Page(int page, bool more, params string[] slugs) =>
        new JsonObject
        {
            ["pagination"] = new JsonObject
            {
                ["page"] = page,
                ["has_more"] = more
            },
            ["data"] = new JsonArray([.. slugs.Select(s => (JsonNode)new JsonObject
            {
                ["slug"] = s,
                ["name"] = s.ToUpperInvariant(),
                ["model_creator"] = new JsonObject { ["name"] = "Maker" },
                ["evaluations"] = new JsonObject
                {
                    ["artificial_analysis_intelligence_index"] = 42.5,
                    ["artificial_analysis_engineering_index"] = null
                },
                ["performance"] = new JsonObject
                {
                    ["median_output_tokens_per_second"] = 100,
                    ["median_time_to_first_token_seconds"] = 1.5
                },
                ["artificial_analysis_intelligence_index_cost"] = new JsonObject { ["cost_per_task"] = new JsonObject { ["total_cost"] = 0.5 } },
            })])
        }.ToJsonString();

    [Fact]
    public async Task The_download_reads_every_page_sends_the_key_only_in_the_header_and_saves_the_rows()
    {
        var http = new Fake(r => r.RequestUri!.Query.Contains("page=1") ? Json(Page(1, true, "m-one", "m-two")) : Json(Page(2, false, "m-three")));
        var file = Path.Combine(_dir, "benchmarks.json");
        var r = await BenchmarkUpdate.RunAsync(new HttpClient(http), " secret-key ", file, TestContext.Current.CancellationToken);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(3, r.Count);
        Assert.Equal(2, http.Requests.Count);
        Assert.All(http.Requests, q => Assert.Equal("artificialanalysis.ai", q.RequestUri!.Host));
        Assert.All(http.Requests, q => Assert.DoesNotContain("secret-key", q.RequestUri!.ToString()));
        Assert.All(http.Requests, q => Assert.Equal("secret-key", q.Headers.GetValues("x-api-key").Single()));
        var rows = BenchmarkStore.Load(file);
        Assert.Equal(["m-one", "m-two", "m-three"], rows.Select(b => b.Slug));
        Assert.Equal((42.5, 100.0, 1.5, 0.5), (rows[0].Intelligence, rows[0].Speed, rows[0].FirstToken, rows[0].CostPerTask));
        Assert.Null(rows[0].Engineering);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "does not accept")]
    [InlineData(HttpStatusCode.Forbidden, "free list")]
    [InlineData(HttpStatusCode.TooManyRequests, "request limit")]
    public async Task A_refused_download_keeps_the_old_copy_and_the_message_never_holds_the_key(HttpStatusCode code, string text)
    {
        var file = Path.Combine(_dir, "benchmarks.json");
        BenchmarkStore.Save([B("old", 1)], file);
        var r = await BenchmarkUpdate.RunAsync(new HttpClient(new Fake(_ => Json("{}", code))), "secret-key", file, TestContext.Current.CancellationToken);
        Assert.False(r.Ok);
        Assert.Contains(text, r.Message);
        Assert.DoesNotContain("secret-key", r.Message);
        Assert.Equal(["old"], BenchmarkStore.Load(file).Select(b => b.Slug));
    }

    [Fact]
    public async Task No_key_means_no_request()
    {
        var http = new Fake(_ => Json("{}"));
        var r = await BenchmarkUpdate.RunAsync(new HttpClient(http), "  ", Path.Combine(_dir, "b.json"), TestContext.Current.CancellationToken);
        Assert.False(r.Ok);
        Assert.Empty(http.Requests);
    }
}
