using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Providers for live limits. Every endpoint here is the one the app's own client calls and is undocumented: replies are read leniently,
/// and anything not recognized is left out rather than guessed.
/// </summary>
public static class LimitProviders
{
    public static readonly ILimitProvider[] All = { new ClaudeLimits(), new CodexLimits(), new CopilotLimits(), new CursorLimits(), new GeminiLimits() };
    public static ILimitProvider? Find(string? id) => id == null ? null : All.FirstOrDefault(p => p.Id == id);

    internal static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    internal static JsonDocument? ReadJson(string path)
    {
        if (!File.Exists(path)) return null;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonDocument.Parse(fs, Lenient);
    }

    // Null when missing (LogParsers' Str/Num return "" and 0), so absent fields are told apart from zero
    internal static string? OptStr(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    internal static double? OptNum(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    /// <summary>The claims of a JWT (not verified: only read for its expiry and plan).</summary>
    public static JsonElement? JwtClaims(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var s = parts[1].Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(s));
            return doc.RootElement.Clone();
        }
        catch (Exception e) when (e is FormatException or JsonException) { return null; }
    }

    public static DateTime? JwtExpiry(string token) => JwtClaims(token)?.OptNum("exp") is { } exp ? DateTimeOffset.FromUnixTimeSeconds((long)exp).UtcDateTime : null;

    internal static async Task<string> Send(HttpClient http, HttpRequestMessage req, CancellationToken ct)
    {
        req.Headers.UserAgent.ParseAdd("AiStackprint/1.0");
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new LimitHttpException(res.StatusCode, res.Headers.RetryAfter?.Delta ?? (res.Headers.RetryAfter?.Date is { } d ? d - DateTimeOffset.UtcNow : null));
        return await res.Content.ReadAsStringAsync(ct);
    }

    internal static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' ');

    internal static DateTime? Time(JsonElement e, string name) =>
        e.OptStr(name) is { } s && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t.LocalDateTime
        : e.OptNum(name) is { } n && n > 1e9 ? DateTimeOffset.FromUnixTimeSeconds((long)(n > 1e12 ? n / 1000 : n)).LocalDateTime : null;
}

/// <summary>Claude Code: ~/.claude/.credentials.json (claudeAiOauth), GET api.anthropic.com/api/oauth/usage — the reply /usage shows.</summary>
public class ClaudeLimits : ILimitProvider
{
    public string Id => "claude";
    public string Host => "api.anthropic.com";

    public Credential? Credential(string home)
    {
        var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } c ? c : Path.Combine(home, ".claude");
        var file = Path.Combine(dir, ".credentials.json");
        using var doc = LimitProviders.ReadJson(file);
        var o = doc?.RootElement.Get("claudeAiOauth") ?? default;
        if (o.OptStr("accessToken") is not { Length: > 0 } token) return null;
        DateTime? exp = o.OptNum("expiresAt") is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime : null;
        return new(token, exp, file, Plan(o.OptStr("subscriptionType"), o.OptStr("rateLimitTier")));
    }

    /// <summary>"max" + "default_claude_max_20x" → "Max 20x".</summary>
    public static string? Plan(string? type, string? tier)
    {
        if (string.IsNullOrEmpty(type)) return null;
        var mult = tier is { } t && System.Text.RegularExpressions.Regex.Match(t, @"(\d+x)\b") is { Success: true } m ? " " + m.Groups[1].Value : "";
        return LimitProviders.Cap(type) + mult;
    }

    public async Task<LimitSnapshot> Fetch(HttpClient http, Credential c, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.Token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        return Parse(await LimitProviders.Send(http, req, ct), DateTime.Now);
    }

    /// <summary>Every object with a numeric "utilization" is a window (five_hour, seven_day, per-model ones); null windows are left out.</summary>
    public static LimitSnapshot Parse(string json, DateTime now)
    {
        using var doc = JsonDocument.Parse(json);
        var windows = new List<LimitWindow>(); string? note = null;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Name == "extra_usage" && p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("is_enabled", out var on) && on.ValueKind == JsonValueKind.True) note = "Extra usage is on.";
            if (p.Value.OptNum("utilization") is not { } u) continue;
            var name = p.Name switch { "five_hour" => "5-hour", "seven_day" => "Weekly", "extra_usage" => "Extra usage", _ when p.Name.StartsWith("seven_day_") => "Weekly · " + LimitProviders.Cap(p.Name[10..]), _ => LimitProviders.Cap(p.Name) };
            windows.Add(new(name, u, LimitProviders.Time(p.Value, "resets_at")));
        }
        return new(null, windows, now, note);
    }
}

/// <summary>Codex: the rate_limits it writes in its session logs (no network); with live limits on, also GET chatgpt.com/backend-api/wham/usage with ~/.codex/auth.json.</summary>
public class CodexLimits : ILimitProvider
{
    public string Id => "codex";
    public string Host => "chatgpt.com";

    public Credential? Credential(string home)
    {
        var dir = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".codex");
        var file = Path.Combine(dir, "auth.json");
        using var doc = LimitProviders.ReadJson(file);
        var t = doc?.RootElement.Get("tokens") ?? default;
        if (t.OptStr("access_token") is not { Length: > 0 } token) return null;   // API-key sign-in: no plan limits
        var claims = t.OptStr("id_token") is { } id ? LimitProviders.JwtClaims(id) : null;
        var plan = claims?.Get("https://api.openai.com/auth").OptStr("chatgpt_plan_type");
        var extra = t.OptStr("account_id") is { } acc ? new Dictionary<string, string> { ["account"] = acc } : null;
        return new(token, LimitProviders.JwtExpiry(token), file, plan == null ? null : LimitProviders.Cap(plan), extra);
    }

    public LimitSnapshot? FromLogs(AppDefinition d, IReadOnlyList<string> projects)
    {
        var files = LogReader.Find(d, projects).Where(f => LogParsers.Find(f.Format) != null).Take(3).ToList();
        var samples = files.SelectMany(f => LogParsers.Find(f.Format)!.Limits(LogReader.Lines(f.Path, 2 << 20))).ToList();
        if (samples.Count == 0) return null;
        var latest = samples.GroupBy(s => s.Name).Select(g => g.MaxBy(s => s.Time)!).OrderBy(s => s.WindowMinutes).ToList();
        return new(null, latest.Select(s => new LimitWindow(s.Name, s.ResetsAt < DateTime.Now ? 0 : s.UsedPercent, s.ResetsAt)).ToList(), latest.Max(s => s.Time), null, true);
    }

    public async Task<LimitSnapshot> Fetch(HttpClient http, Credential c, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.Token);
        if (c.Extra?.GetValueOrDefault("account") is { } acc) req.Headers.Add("ChatGPT-Account-Id", acc);
        return Parse(await LimitProviders.Send(http, req, ct), DateTime.Now);
    }

    /// <summary>Any object with "used_percent" is a window; its length (seconds or minutes) names it.</summary>
    public static LimitSnapshot Parse(string json, DateTime now)
    {
        using var doc = JsonDocument.Parse(json);
        var windows = new List<LimitWindow>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.OptNum("used_percent") is { } pct)
                {
                    double minutes = e.OptNum("limit_window_seconds") is { } s ? s / 60 : e.OptNum("window_minutes") ?? 0;
                    var reset = LimitProviders.Time(e, "reset_at") ?? LimitProviders.Time(e, "resets_at") ?? (e.OptNum("reset_after_seconds") is { } after ? now.AddSeconds(after) : null);
                    windows.Add(new(minutes switch { 300 => "5-hour", 10080 => "Weekly", > 0 => $"{minutes:0}-minute", _ => "Limit" }, pct, reset));
                }
                foreach (var p in e.EnumerateObject()) Walk(p.Value);
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Walk(v);
        }
        Walk(doc.RootElement);
        var plan = doc.RootElement.OptStr("plan_type");
        return new(plan == null ? null : LimitProviders.Cap(plan), windows, now);
    }
}
