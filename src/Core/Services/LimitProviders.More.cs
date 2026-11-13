// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Data.Sqlite;

namespace AiStackprint.Services;

/// <summary>GitHub Copilot: the GitHub token the Copilot CLI or editor plugin saved, GET api.github.com/copilot_internal/user (plan and monthly quotas).</summary>
public class CopilotLimits : ILimitProvider
{
    public string Id => "copilot";
    public string Host => "api.github.com";

    public Credential? Credential(string home)
    {
        // Copilot CLI (when it is not using the keychain), then the editor plugins' apps.json
        var cli = Path.Combine(Environment.GetEnvironmentVariable("COPILOT_HOME") is { Length: > 0 } h ? h : Path.Combine(home, ".copilot"), "config.json");
        using (var doc = LimitProviders.ReadJson(cli))
        {
            if (doc?.RootElement.Get("copilotTokens") is { ValueKind: JsonValueKind.Object } tokens)
            {
                foreach (var t in tokens.EnumerateObject())
                {
                    if (Token(t.Value) is { } tok)
                    {
                        return new(tok, null, cli, null);
                    }
                }
            }
        }

        var apps = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "github-copilot", "apps.json")
            : Path.Combine(home, ".config", "github-copilot", "apps.json");
        using (var doc = LimitProviders.ReadJson(apps))
        {
            if (doc?.RootElement is { ValueKind: JsonValueKind.Object } root)
            {
                foreach (var a in root.EnumerateObject().Where(a => a.Name.StartsWith("github.com")).OrderBy(a => a.Name, StringComparer.Ordinal))
                {
                    if (a.Value.OptStr("oauth_token") is { Length: > 0 } tok)
                    {
                        return new(tok, null, apps, null);
                    }
                }
            }
        }

        return null;
    }

    private static string? Token(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString() : v.OptStr("token") ?? v.OptStr("value") ?? v.OptStr("oauth_token");

    public async Task<LimitSnapshot> Fetch(HttpClient http, Credential c, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/user");
        req.Headers.Authorization = new AuthenticationHeaderValue("token", c.Token);
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Add("X-GitHub-Api-Version", "2025-04-01");
        return Parse(await LimitProviders.Send(http, req, ct).ConfigureAwait(false), DateTime.Now);
    }

    /// <summary>copilot_plan, and quota_snapshots: used = 100 − percent_remaining (or from entitlement and remaining); unlimited ones are left out.</summary>
    public static LimitSnapshot Parse(string json, DateTime now)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var reset = LimitProviders.Time(r, "quota_reset_date_utc") ?? LimitProviders.Time(r, "quota_reset_date");
        var windows = new List<LimitWindow>();
        if (r.Get("quota_snapshots") is { ValueKind: JsonValueKind.Object } q)
        {
            foreach (var s in q.EnumerateObject().OrderBy(s => s.Name == "premium_interactions" ? 0 : 1).ThenBy(s => s.Name))
            {
                if (s.Value.ValueKind != JsonValueKind.Object || s.Value.TryGetProperty("unlimited", out var u) && u.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                double? used = s.Value.OptNum("percent_remaining") is { } left ? 100 - left
                    : s.Value.OptNum("entitlement") is { } ent && ent > 0 && s.Value.OptNum("remaining") is { } rem ? 100 * (ent - rem) / ent : null;
                if (used != null)
                {
                    windows.Add(new(s.Name == "premium_interactions" ? "Premium requests" : LimitProviders.Cap(s.Name), used.Value, reset));
                }
            }
        }

        var plan = r.OptStr("copilot_plan");
        return new(plan == null ? null : LimitProviders.Cap(plan), windows, now);
    }
}

/// <summary>
/// Cursor: the session token the IDE keeps in state.vscdb (read-only), GET cursor.com/api/usage-summary as its dashboard does.
/// The reply's shape is not published: only percentages and used/limit pairs found in it are shown.
/// </summary>
public class CursorLimits : ILimitProvider
{
    public string Id => "cursor";
    public string Host => "cursor.com";

    private static string Db(string home) =>
        OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb")
        : OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Application Support", "Cursor", "User", "globalStorage", "state.vscdb")
        : Path.Combine(home, ".config", "Cursor", "User", "globalStorage", "state.vscdb");

    public Credential? Credential(string home)
    {
        var db = Db(home);
        if (!File.Exists(db))
        {
            return null;
        }

        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        con.Open();
        string? Get(string key)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT value FROM ItemTable WHERE key = $k LIMIT 1";
            cmd.Parameters.AddWithValue("$k", key);
            return (cmd.ExecuteScalar() as string)?.Trim('"');
        }
        if (Get("cursorAuth/accessToken") is not { Length: > 0 } token)
        {
            return null;
        }

        var sub = LimitProviders.JwtClaims(token)?.OptStr("sub") ?? "";
        var user = sub.Contains('|') ? sub[(sub.LastIndexOf('|') + 1)..] : sub;
        var plan = Get("cursorAuth/stripeMembershipType");
        return new(token, LimitProviders.JwtExpiry(token), db, plan == null ? null : LimitProviders.Cap(plan), new Dictionary<string, string> { ["user"] = user });
    }

    public async Task<LimitSnapshot> Fetch(HttpClient http, Credential c, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://cursor.com/api/usage-summary");
        req.Headers.Add("Cookie", $"WorkosCursorSessionToken={c.Extra?.GetValueOrDefault("user")}%3A%3A{c.Token}");
        return Parse(await LimitProviders.Send(http, req, ct).ConfigureAwait(false), DateTime.Now);
    }

    public static LimitSnapshot Parse(string json, DateTime now)
    {
        using var doc = JsonDocument.Parse(json);
        var windows = new List<LimitWindow>(); DateTime? reset = null; string? plan = null;
        void Walk(JsonElement e, string path)
        {
            if (e.ValueKind == JsonValueKind.Array) { foreach (var v in e.EnumerateArray()) { Walk(v, path); } return; }
            if (e.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (e.OptNum("used") is { } used && e.OptNum("limit") is { } limit && limit > 0)
            {
                windows.Add(new(Name(path), 100 * used / limit, null));
            }

            foreach (var p in e.EnumerateObject())
            {
                var lower = p.Name.ToLowerInvariant();
                if (p.Value.ValueKind == JsonValueKind.Number && lower.Contains("percent") && p.Value.GetDouble() is >= 0 and <= 100)
                {
                    windows.Add(new(Name(path == "" ? p.Name : path), p.Value.GetDouble(), null));
                }
                else if (lower is "billingcycleend" or "cycleend" or "resetat")
                {
                    reset ??= LimitProviders.Time(e, p.Name);
                }
                else if (lower is "membershiptype" or "plan" && p.Value.ValueKind == JsonValueKind.String)
                {
                    plan ??= p.Value.GetString();
                }
                else
                {
                    Walk(p.Value, p.Name);
                }
            }
        }
        Walk(doc.RootElement, "");
        var list = windows.DistinctBy(w => w.Name).Select(w => w with { ResetsAt = reset }).ToList();
        return new(plan == null ? null : LimitProviders.Cap(plan), list, now, list.Count == 0 ? "Cursor's reply had no usage figures this app recognizes." : null);
    }

    private static string Name(string key) => LimitProviders.Cap(System.Text.RegularExpressions.Regex.Replace(key, "(?<=[a-z])([A-Z])", " $1").ToLowerInvariant());
}
