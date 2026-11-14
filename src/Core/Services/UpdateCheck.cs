// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>
/// Is a newer AI Stackprint released? Asks api.github.com for the latest release of the project (drafts and pre-releases are skipped)
/// and compares its tag (v1.2.3) with this build's version. Only checks; the user downloads the release from its page.
/// Network calls only when the user turned the check on or pressed Check now; automatic checks at most once a day.
/// </summary>
public static class UpdateCheck
{
    public const string Repo = "krurkrit/ai-stackprint";
    public const string Host = "api.github.com";
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>A release on GitHub: its version and its page.</summary>
    public record Release(Version Version, string Url);

    /// <summary>This build's version as shown, e.g. "0.2.0" or "0.2.1-alpha.0.3" (MinVer, from the git tag; every project gets the same).</summary>
    public static string Label { get; } = LabelOf(typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>This build's version (major.minor.patch) for comparing with releases. Not the assembly version: MinVer keeps that at major.0.0.</summary>
    public static Version Current { get; } = ParseTag(Label.Split('-')[0]) ?? new Version(0, 0, 0);

    /// <summary>Informational version without the build metadata MinVer and the SDK add ("+commit").</summary>
    public static string LabelOf(string? informational) => informational?.Split('+')[0].Trim() is { Length: > 0 } v ? v : "0.0.0";

    /// <summary>The automatic check is due: never checked, or the last check was at least <see cref="Interval"/> ago (or in the future: clock changed).</summary>
    public static bool Due(DateTime? lastUtc, DateTime nowUtc) => lastUtc is not { } t || nowUtc - t >= Interval || t > nowUtc;

    /// <summary>"v1.2.3" or "1.2.3" as a version; null for anything else ("v1.2.3-beta", "latest", "").</summary>
    public static Version? ParseTag(string? tag) =>
        Version.TryParse(tag?.Trim().TrimStart('v', 'V'), out var v) ? Normalize(v) : null;

    /// <summary>The release in a GitHub "latest release" reply; null when it is a draft or pre-release, has no usable tag, or its page is not on this project.</summary>
    public static Release? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || Flag(r, "draft") || Flag(r, "prerelease"))
        {
            return null;
        }

        var v = ParseTag(Str(r, "tag_name"));
        var url = Str(r, "html_url");
        return v != null && url != null && url.StartsWith($"https://github.com/{Repo}/", StringComparison.Ordinal) ? new Release(v, url) : null;
    }

    /// <summary>The release when it is newer than <paramref name="current"/>; otherwise null.</summary>
    public static Release? Newer(Release? latest, Version current) => latest != null && latest.Version > Normalize(current) ? latest : null;

    /// <summary>The latest release on GitHub. Throws <see cref="HttpRequestException"/> or <see cref="JsonException"/> when it cannot be read; null when there is none.</summary>
    public static async Task<Release?> LatestAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{Host}/repos/{Repo}/releases/latest");
        req.Headers.UserAgent.ParseAdd("AiStackprint/" + Current);   // GitHub refuses requests without one
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;   // no release published yet
        }

        if (!res.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub answered {(int)res.StatusCode} {res.ReasonPhrase}.", null, res.StatusCode);
        }

        return Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
