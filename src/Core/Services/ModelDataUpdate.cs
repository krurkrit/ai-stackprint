// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiStackprint.Services;

/// <summary>What a model data check did.</summary>
/// <param name="Saved">A new copy was saved to cache/models.json.</param>
/// <param name="Ok">GitHub answered with usable data or "not changed"; false: the last good copy stays and the check is tried again later.</param>
/// <param name="Message">For Settings, e.g. "New model data from 2026-11-22." or "GitHub answered 403 (rate limit)."</param>
/// <param name="Etag">GitHub's ETag of the saved copy, sent as If-None-Match next time.</param>
public sealed record ModelDataResult(bool Saved, bool Ok, string Message, string? Etag);

/// <summary>
/// Downloads default/models.json of this project from api.github.com into cache/models.json, so prices and sizes follow the weekly data
/// without a new release. Only when the user turned on "Check GitHub for updates and model data" (at most once a day) or pressed Check now;
/// nothing about the user is sent. A copy is saved only when it is valid; on any failure the last good copy stays.
/// </summary>
public static class ModelDataUpdate
{
    public const string RepoPath = "default/models.json";
    public const int MaxBytes = 2_000_000;
    /// <summary>The Usage page notes model data older than this: the weekly data has not reached this copy.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(60);
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static Uri Url { get; } = new($"https://{UpdateCheck.Host}/repos/{UpdateCheck.Repo}/contents/{RepoPath}");

    /// <summary>The data date is more than <see cref="StaleAfter"/> before <paramref name="nowUtc"/>; false when it has no date.</summary>
    public static bool Stale(string? updated, DateTime nowUtc) =>
        DateOnly.TryParseExact(updated, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && nowUtc.Date - d.ToDateTime(TimeOnly.MinValue) > StaleAfter;

    public static Task<ModelDataResult> RunAsync(string? etag, CancellationToken ct = default) => RunAsync(_http, ModelCatalog.CacheFile, etag, ct);

    /// <summary>One request; never throws for network or data problems (they are the result's message).</summary>
    public static async Task<ModelDataResult> RunAsync(HttpClient http, string cacheFile, string? etag, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Url);
        req.Headers.UserAgent.ParseAdd("AiStackprint/" + UpdateCheck.Current);   // GitHub refuses requests without one
        req.Headers.Accept.ParseAdd("application/vnd.github.raw+json");          // the file itself, not its metadata
        if (etag != null && File.Exists(cacheFile))
        {
            req.Headers.TryAddWithoutValidation("If-None-Match", etag);   // only when the copy it describes is still there
        }

        string body;
        string? newTag;
        try
        {
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.NotModified)
            {
                return new(false, true, "The model data is up to date.", etag);
            }

            if (!res.IsSuccessStatusCode)
            {
                var why = res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests ? " (GitHub's limit for requests without sign-in; tried again tomorrow)" : "";
                return Fail($"GitHub answered {(int)res.StatusCode}{why}.");
            }

            if (res.Content.Headers.ContentLength > MaxBytes)
            {
                return Fail("The model data is larger than expected; not used.");
            }

            newTag = res.Headers.ETag?.ToString();
            body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("GitHub did not answer in time.");
        }
        catch (HttpRequestException e)
        {
            return Fail("Could not reach GitHub: " + e.Message);
        }

        if (body.Length > MaxBytes || Check(body) is not { } updated)
        {
            return Fail(body.Length > MaxBytes ? "The model data is larger than expected; not used." : Problem(body));
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cacheFile))!);
            var tmp = cacheFile + ".tmp";
            await File.WriteAllTextAsync(tmp, body, ct).ConfigureAwait(false);
            File.Move(tmp, cacheFile, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail("Could not save the model data: " + e.Message);
        }

        return new(true, true, $"Model data from {updated} saved.", newTag);
    }

    private static ModelDataResult Fail(string message) => new(false, false, message, null);

    /// <summary>The data date when <paramref name="body"/> is model data this app can use: version 1, a date, at least one model, each with a provider.</summary>
    internal static string? Check(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject o || o["version"] is not JsonValue v || !v.TryGetValue(out int version) || version != 1
                || o["updated"] is not JsonValue u || !u.TryGetValue(out string? updated) || !DateOnly.TryParseExact(updated, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || o["models"] is not JsonObject { Count: > 0 } models)
            {
                return null;
            }

            return models.All(m => m.Value is JsonObject mo && mo["provider"] is JsonValue p && p.TryGetValue(out string? _)) ? updated : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Problem(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["version"] is JsonValue v && v.TryGetValue(out int n) && n > 1
                ? "The model data on GitHub needs a newer AI Stackprint; the copy you have is kept."
                : "The model data on GitHub is not in the expected form; the copy you have is kept.";
        }
        catch (JsonException) { return "The model data on GitHub is not valid JSON; the copy you have is kept."; }
    }
}
