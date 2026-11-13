// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>
/// Gemini CLI signed in with Google: ~/.gemini/oauth_creds.json (newer versions keep it in the keychain instead),
/// POST cloudcode-pa.googleapis.com loadCodeAssist (project, tier), then retrieveUserQuota (remaining fraction per model).
/// </summary>
public class GeminiLimits : ILimitProvider
{
    public string Id => "gemini";
    public string Host => "cloudcode-pa.googleapis.com";
    const string Api = "https://cloudcode-pa.googleapis.com/v1internal:";

    public Credential? Credential(string home)
    {
        var file = Path.Combine(home, ".gemini", "oauth_creds.json");
        using var doc = LimitProviders.ReadJson(file);
        if (doc?.RootElement.OptStr("access_token") is not { Length: > 0 } token)
        {
            return null;
        }

        DateTime? exp = doc.RootElement.OptNum("expiry_date") is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime : null;
        var project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT_ID");
        return new(token, exp, file, null, project == null ? null : new Dictionary<string, string> { ["project"] = project });
    }

    public async Task<LimitSnapshot> Fetch(HttpClient http, Credential c, CancellationToken ct)
    {
        var assist = await Post(http, c, "loadCodeAssist", "{\"metadata\":{\"ideType\":\"IDE_UNSPECIFIED\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}", ct).ConfigureAwait(false);
        using var a = JsonDocument.Parse(assist);
        var tier = a.RootElement.Get("currentTier");
        var project = c.Extra?.GetValueOrDefault("project") ?? a.RootElement.OptStr("cloudaicompanionProject") ?? a.RootElement.Get("cloudaicompanionProject").OptStr("id");
        var plan = tier.OptStr("name") ?? tier.OptStr("id");
        if (project == null)
        {
            return new(plan, new(), DateTime.Now, "No Google Cloud project for quota.");
        }

        var quota = await Post(http, c, "retrieveUserQuota", JsonSerializer.Serialize(new { project }), ct).ConfigureAwait(false);
        return Parse(quota, DateTime.Now) with { Plan = plan };
    }

    static async Task<string> Post(HttpClient http, Credential c, string method, string body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Api + method) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.Token);
        return await LimitProviders.Send(http, req, ct).ConfigureAwait(false);
    }

    /// <summary>buckets[]: used = (1 − remainingFraction) × 100 per model.</summary>
    public static LimitSnapshot Parse(string json, DateTime now)
    {
        using var doc = JsonDocument.Parse(json);
        var windows = new List<LimitWindow>();
        if (doc.RootElement.Get("buckets") is { ValueKind: JsonValueKind.Array } buckets)
        {
            foreach (var b in buckets.EnumerateArray())
            {
                if (b.OptNum("remainingFraction") is { } left)
                {
                    windows.Add(new(b.OptStr("modelId") ?? "Requests", Math.Clamp((1 - left) * 100, 0, 100), LimitProviders.Time(b, "resetTime")));
                }
            }
        }

        return new(null, windows.DistinctBy(w => w.Name).ToList(), now);
    }
}
