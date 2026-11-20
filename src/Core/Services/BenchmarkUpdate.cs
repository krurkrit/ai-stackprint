// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiStackprint.Services;

/// <summary>What a benchmark download did. <see cref="Message"/> never contains the key.</summary>
public sealed record BenchmarkResult(bool Ok, string Message, int Count);

/// <summary>
/// Downloads benchmark scores from Artificial Analysis (artificialanalysis.ai, free API: the key is sent in the x-api-key header, credit is required) into cache/benchmarks.json.
/// Only when the user turned on Settings › Benchmarks and entered a key: at most once a day on its own, or when Refresh is pressed. Nothing about the user is sent.
/// The key is kept in state.json (never exported), is never shown or logged, and is sent to artificialanalysis.ai only. On any failure the last good copy stays.
/// </summary>
public static class BenchmarkUpdate
{
    public const string Host = "artificialanalysis.ai";
    public const string Site = "https://artificialanalysis.ai";
    private const int MaxPages = 10, MaxBytes = 3_000_000;
    /// <summary>The HTTP client this feature uses. Tests give it one that answers from a function, so no request leaves the machine.</summary>
    internal static HttpClient Http { get; set; } = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static bool Due(DateTime? lastUtc, DateTime nowUtc) => UpdateCheck.Due(lastUtc, nowUtc);

    public static Task<BenchmarkResult> RunAsync(string key, CancellationToken ct = default) => RunAsync(Http, key, BenchmarkStore.CacheFile, ct);

    /// <summary>All pages of the free language-model list; never throws for network or data problems (they are the result's message).</summary>
    public static async Task<BenchmarkResult> RunAsync(HttpClient http, string key, string cacheFile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new(false, "Enter an API key first.", 0);
        }

        var rows = new List<Benchmark>();
        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{Site}/api/v2/language/models/free?page={page}");
                req.Headers.TryAddWithoutValidation("x-api-key", key.Trim());
                using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    return new(false, Failure(res.StatusCode), 0);
                }

                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (body.Length > MaxBytes || JsonNode.Parse(body) is not JsonObject o || o["data"] is not JsonArray data)
                {
                    return new(false, "Artificial Analysis answered in an unexpected form; the copy you have is kept.", 0);
                }

                rows.AddRange(data.OfType<JsonObject>().Select(Parse).OfType<Benchmark>());
                if (o["pagination"]?["has_more"] is not JsonValue more || !more.TryGetValue(out bool hasMore) || !hasMore)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, "Artificial Analysis did not answer in time.", 0); }
        catch (HttpRequestException e) { return new(false, "Could not reach Artificial Analysis: " + e.Message, 0); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return new(false, "Artificial Analysis answered in an unexpected form; the copy you have is kept.", 0); }

        if (rows.Count == 0)
        {
            return new(false, "Artificial Analysis returned no models; the copy you have is kept.", 0);
        }

        try { BenchmarkStore.Save(rows, cacheFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(false, "Could not save the benchmarks: " + e.Message, 0); }
        return new(true, $"{rows.Count:N0} models saved.", rows.Count);
    }

    private static string Failure(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized => "Artificial Analysis does not accept this key.",
        HttpStatusCode.Forbidden => "This key may not use the free list (a different plan, or the key is not valid).",
        HttpStatusCode.TooManyRequests => "Artificial Analysis asked to wait (request limit); tried again tomorrow.",
        _ => $"Artificial Analysis answered {(int)code}."
    };

    /// <summary>One model of the reply; null when it has no slug.</summary>
    internal static Benchmark? Parse(JsonObject m)
    {
        static double? N(JsonNode? n) => n is JsonValue v && v.TryGetValue(out double d) ? d : null;
        if (m["slug"]?.GetValue<string>() is not { Length: > 0 } slug)
        {
            return null;
        }

        var ev = m["evaluations"];
        return new(slug, m["name"]?.GetValue<string>() ?? slug, m["model_creator"]?["name"]?.GetValue<string>() ?? "",
            N(ev?["artificial_analysis_intelligence_index"]), N(ev?["artificial_analysis_engineering_index"]), N(ev?["artificial_analysis_finance_and_accounting_index"]),
            N(ev?["artificial_analysis_legal_index"]), N(ev?["artificial_analysis_strategy_and_ops_index"]),
            N(m["performance"]?["median_output_tokens_per_second"]), N(m["performance"]?["median_time_to_first_token_seconds"]),
            N(m["artificial_analysis_intelligence_index_cost"]?["cost_per_task"]?["total_cost"]));
    }
}
