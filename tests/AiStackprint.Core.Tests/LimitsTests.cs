// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

[Collection("Environment")]
public sealed class LimitsTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aistack-").FullName;
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
        Directory.Delete(_tmp, true);
    }
    private static readonly DateTime _now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Expired_sign_in_message_says_how_long_ago_and_how_to_renew()
    {
        var m = LimitsService.ExpiredMessage("Claude Code", "claude", DateTime.UtcNow.AddHours(-27));
        Assert.Contains("expired 27 hours ago", m);
        Assert.Contains("Run `claude` once to renew.", m);
        Assert.Contains("Open Cursor once to renew.", LimitsService.ExpiredMessage("Cursor", null, DateTime.UtcNow.AddHours(-2)));
        Assert.Contains("3 days ago", LimitsService.ExpiredMessage("Claude Code", "claude", DateTime.UtcNow.AddDays(-3).AddMinutes(-5)));
    }

    [Fact]
    public void Claude_reply_windows_plan_and_extra_usage()
    {
        var s = ClaudeLimits.Parse("""
            { "five_hour": { "utilization": 35.0, "resets_at": "2026-11-12T05:00:00+00:00" }, "seven_day": { "utilization": 49.5, "resets_at": null },
              "seven_day_opus": null, "seven_day_sonnet": { "utilization": 1.0 }, "iguana_necktie": null,
              "extra_usage": { "is_enabled": true, "monthly_limit": 100000, "used_credits": 0.0, "utilization": null } }
            """, _now);
        Assert.Equal(["5-hour", "Weekly", "Weekly · Sonnet"], s.Windows.Select(w => w.Name));
        Assert.Equal(35, s.Windows[0].UsedPercent);
        Assert.NotNull(s.Windows[0].ResetsAt);
        Assert.Null(s.Windows[1].ResetsAt);
        Assert.Equal("Extra usage is on.", s.Note);
        Assert.Equal("Max 20x", ClaudeLimits.Plan("max", "default_claude_max_20x"));
        Assert.Equal("Pro", ClaudeLimits.Plan("pro", null));
    }

    [Fact]
    public void Claude_credential_from_file()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(_tmp, ".claude"));
        Directory.CreateDirectory(Path.Combine(_tmp, ".claude"));
        File.WriteAllText(Path.Combine(_tmp, ".claude", ".credentials.json"),
            """{ "claudeAiOauth": { "accessToken": "tok", "expiresAt": 1893456000000, "subscriptionType": "max", "rateLimitTier": "default_claude_max_5x" } }""");
        var c = new ClaudeLimits().Credential(_tmp)!;
        Assert.Equal("tok", c.Token);
        Assert.Equal("Max 5x", c.Plan);
        Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), c.Expires);
    }

    [Fact]
    public void Codex_reply_names_windows_by_length()
    {
        var s = CodexLimits.Parse("""
            { "plan_type": "plus", "rate_limit": { "allowed": true,
              "primary_window": { "used_percent": 42, "limit_window_seconds": 18000, "reset_at": 1794470400 },
              "secondary_window": { "used_percent": 7, "limit_window_seconds": 604800, "reset_after_seconds": 3600 } } }
            """, _now);
        Assert.Equal("Plus", s.Plan);
        Assert.Equal([("5-hour", 42.0), ("Weekly", 7.0)], s.Windows.Select(w => (w.Name, w.UsedPercent)));
        Assert.Equal(_now.AddHours(1), s.Windows[1].ResetsAt);
    }

    [Fact]
    public void Copilot_quota_snapshots_skip_unlimited()
    {
        var s = CopilotLimits.Parse("""
            { "copilot_plan": "individual_pro", "quota_reset_date": "2026-12-01",
              "quota_snapshots": { "chat": { "unlimited": true }, "completions": { "entitlement": 200, "remaining": 150, "unlimited": false },
                                   "premium_interactions": { "entitlement": 300, "remaining": 240, "percent_remaining": 80, "unlimited": false } } }
            """, _now);
        Assert.Equal("Individual pro", s.Plan);
        Assert.Equal([("Premium requests", 20.0), ("Completions", 25.0)], s.Windows.Select(w => (w.Name, w.UsedPercent)));
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Local), s.Windows[0].ResetsAt!.Value.Date);
    }

    [Fact]
    public void Gemini_buckets_and_cursor_figures()
    {
        var g = GeminiLimits.Parse("""{ "buckets": [ { "modelId": "gemini-2.5-pro", "remainingFraction": 0.75, "resetTime": "2026-11-13T00:00:00Z" }, { "remainingFraction": 1 } ] }""", _now);
        Assert.Equal([("gemini-2.5-pro", 25.0), ("Requests", 0.0)], g.Windows.Select(w => (w.Name, w.UsedPercent)));

        var c = CursorLimits.Parse("""{ "membershipType": "pro", "billingCycleEnd": "2026-12-01T00:00:00Z", "individualUsage": { "plan": { "used": 500, "limit": 2000 } } }""", _now);
        Assert.Equal("Pro", c.Plan);
        var w = Assert.Single(c.Windows);
        Assert.Equal(25, w.UsedPercent);
        Assert.NotNull(w.ResetsAt);
        Assert.NotNull(CursorLimits.Parse("{}", _now).Note);   // nothing recognized: says so instead of guessing
    }

    [Fact]
    public void Jwt_expiry_and_pace_forecast()
    {
        static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000).UtcDateTime, LimitProviders.JwtExpiry($"{B64("{}")}.{B64("{\"exp\":1893456000}")}.sig"));
        Assert.Null(LimitProviders.JwtExpiry("not-a-jwt"));

        var t0 = _now;
        var t1 = _now.AddHours(1);
        Assert.Equal(t1.AddHours(8), LimitsService.FullAt(new("5-hour", 10, null), t0, new("5-hour", 20, null), t1));
        Assert.Null(LimitsService.FullAt(new("5-hour", 10, null), t0, new("5-hour", 20, t1.AddHours(2)), t1));   // resets first
        Assert.Null(LimitsService.FullAt(new("5-hour", 20, null), t0, new("5-hour", 20, null), t1));             // not rising
    }

    [Fact]
    public void Service_makes_no_call_until_turned_on()
    {
        var settings = SettingsStore.Defaults();
        var svc = new LimitsService(() => settings);
        var done = new ManualResetEventSlim();
        svc.Updated += _ => { if (!svc.Status("claude-code").Busy) { done.Set(); } };
        svc.Request(new AppDefinition
        {
            Id = "claude-code",
            Name = "Claude Code",
            Limits = "claude"
        }, []);
        Assert.True(done.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var st = svc.Status("claude-code");
        Assert.Equal(LimitState.Off, st.State);
        Assert.Null(st.Data);
        svc.Request(new AppDefinition
        {
            Id = "claude-code",
            Name = "Claude Code",
            Limits = "claude"
        }, [], force: true);
        Assert.False(svc.Status("claude-code").Busy);   // at most once a minute, even when forced
        Assert.NotNull(svc.Fetched("claude-code"));
    }

    [Fact]
    public void An_app_without_a_provider_is_never_read_and_errors_get_a_short_message()
    {
        var svc = new LimitsService(SettingsStore.Defaults);
        svc.Request(new AppDefinition
        {
            Id = "x",
            Name = "X",
            Limits = "none"
        }, []);
        Assert.False(svc.Status("x").Busy);
        Assert.Null(svc.Fetched("x"));
        Assert.Equal("The provider did not answer in time.", LimitsService.ShortMessage(new TaskCanceledException()));
        Assert.Equal("The provider answered 500.", LimitsService.ShortMessage(new HttpRequestException("x", null, System.Net.HttpStatusCode.InternalServerError)));
        Assert.Equal("Cannot reach the provider (offline?).", LimitsService.ShortMessage(new HttpRequestException("x")));
        Assert.Equal("Could not read the reply: bad", LimitsService.ShortMessage(new FormatException("bad")));
    }
}

public sealed class ScanAndHealthTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aistack-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    [Fact]
    public void Crash_log_records_and_reports_new_entries_once()
    {
        Assert.False(CrashLog.TakeNew(_tmp));
        CrashLog.Write("The app closed after this error", new InvalidOperationException("boom"), _tmp);
        var text = File.ReadAllText(Path.Combine(_tmp, "crash.log"));
        Assert.Contains("InvalidOperationException: boom", text);
        Assert.True(CrashLog.TakeNew(_tmp));
        Assert.False(CrashLog.TakeNew(_tmp));
    }

    [Fact]
    public void Scan_diff_saves_its_time_in_UTC_and_gives_it_back_in_local_time_also_from_an_older_file_that_held_local_time()
    {
        var file = Path.Combine(_tmp, "last-scan.json");
        var a = new Item("Skill", "a", "", "App", "User", "/s/a/SKILL.md") { App = "app" };
        ScanDiff.Update([a], file);
        Assert.Contains("Z\"", File.ReadAllText(file));   // "2026-11-18T05:00:00.123Z": UTC, no offset to get wrong when the time zone changes

        var older = DateTimeOffset.Now.AddHours(-2);
        File.WriteAllText(file, $$"""{"Time":"{{older:yyyy-MM-ddTHH:mm:ss.fffzzz}}","Items":[]}""");   // what older versions wrote: local time with its offset
        var since = ScanDiff.Update([a], file).Since!.Value;
        Assert.Equal(DateTimeKind.Local, since.Kind);
        Assert.InRange((older.LocalDateTime - since).TotalSeconds, -1, 1);
    }

    [Fact]
    public void Scan_diff_reports_added_removed_and_edited_config()
    {
        var file = Path.Combine(_tmp, "last-scan.json");
        var cfg = Path.Combine(_tmp, "CLAUDE.md");
        File.WriteAllText(cfg, "x");
        var a = new Item("Skill", "a", "", "App", "User", "/s/a/SKILL.md") { App = "app" };
        var c = new Item("Config", "CLAUDE.md", "Instructions", "App", "User", cfg) { App = "app" };
        Assert.Null(ScanDiff.Update([a, c], file).Since);
        File.SetLastWriteTimeUtc(cfg, DateTime.UtcNow.AddMinutes(1));
        var b = a with
        {
            Name = "b",
            Path = "/s/b/SKILL.md"
        };
        var r = ScanDiff.Update([b, c], file);
        Assert.NotNull(r.Since);
        Assert.Equal([("Added", "b"), ("Edited", "CLAUDE.md"), ("Removed", "a")], r.Changes.Select(x => (x.What, x.Name)).OrderBy(x => x.What));
        Assert.Empty(ScanDiff.Update([b, c], file).Changes);

        var state = c with
        {
            Name = ".claude.json",
            AppWritten = true
        };   // the app saves it on its own: edits are not changes
        ScanDiff.Update([state], file);
        File.SetLastWriteTimeUtc(cfg, DateTime.UtcNow.AddMinutes(2));
        Assert.Empty(ScanDiff.Update([state], file).Changes);
    }

    [Fact]
    public void Health_flags_missing_plugins_and_a_large_claude_skill_list()
    {
        var plugin = new Item("Plugin", "gone@market", "", "Claude Code", "User", Path.Combine(_tmp, "nope")) { App = "claude-code" };
        var skills = Enumerable.Range(0, 12).Select(i => new Item("Skill", $"s{i}", new string('d', 900), "Claude Code", "User", Path.Combine(_tmp, $"s{i}", "SKILL.md"))
        {
            App = "claude-code",
            Invokers = ["Agent", "User"]
        }).ToList();
        var issues = Health.Compute([.. skills, plugin]);
        Assert.Contains(issues, h => h.Area == "Plugins" && h.Finding.Warning && h.Finding.Text.Contains("folder is missing"));
        Assert.Contains(issues, h => h.Subject == "Claude Code skill list" && h.Finding.Warning);
    }
}
