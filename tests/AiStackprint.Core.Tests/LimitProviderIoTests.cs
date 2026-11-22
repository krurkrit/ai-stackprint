// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>A stand-in for the network: the provider gets an HttpClient that answers from a function, so no request leaves the machine.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>What each request sent as its body (a request's content is gone once it has been sent).</summary>
    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        return answer(request);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>What each provider reads from the tool's own sign-in files, and what it asks the service.</summary>
[Collection("Environment")]
public sealed class LimitProviderIoTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("aiprov-").FullName;

    public void Dispose()
    {
        foreach (var v in new[] { "COPILOT_HOME", "CODEX_HOME", "GOOGLE_CLOUD_PROJECT", "GOOGLE_CLOUD_PROJECT_ID" })
        {
            Environment.SetEnvironmentVariable(v, null);
        }

        CursorLimits.DbOverride = null;
        Directory.Delete(_home, true);
    }

    private void Write(string rel, string text)
    {
        var path = Path.Combine(_home, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Jwt(string claims) => string.Join('.', B64("""{"alg":"none"}"""), B64(claims), "sig");

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task Copilot_reads_the_cli_token_in_either_form_and_asks_the_github_api()
    {
        var dir = Path.Combine(_home, "cop");
        Environment.SetEnvironmentVariable("COPILOT_HOME", dir);
        var p = new CopilotLimits();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Null(p.Credential(_home));   // no file, no sign-in
        }

        Write("cop/config.json", """{ "copilotTokens": { "github.com:a": { "nothing": 1 }, "github.com:b": "plain-token" } }""");
        Assert.Equal("plain-token", p.Credential(_home)!.Token);
        Write("cop/config.json", """{ "copilotTokens": { "github.com:a": { "token": "object-token" } } }""");
        var c = p.Credential(_home)!;
        Assert.Equal("object-token", c.Token);

        var http = new StubHandler(_ => StubHandler.Json("""{ "copilot_plan": "individual", "quota_snapshots": { "premium_interactions": { "entitlement": 300, "remaining": 150, "percent_remaining": 50 } } }"""));
        var snap = await p.Fetch(new HttpClient(http), c, TestContext.Current.CancellationToken);
        Assert.Equal("token object-token", http.Requests.Single().Headers.Authorization!.ToString());
        Assert.Equal("api.github.com", http.Requests.Single().RequestUri!.Host);
        Assert.NotEmpty(snap.Windows);
    }

    [Fact]
    public void Copilot_falls_back_to_the_editor_plugins_file_where_it_can_be_redirected()
    {
        Environment.SetEnvironmentVariable("COPILOT_HOME", Path.Combine(_home, "none"));
        var apps = OperatingSystem.IsWindows()   // Windows: in local app data (a folder of the test home here)
            ? Path.Combine(PathUtil.SystemFolder(Environment.SpecialFolder.LocalApplicationData), "github-copilot", "apps.json")
            : Path.Combine(_home, ".config", "github-copilot", "apps.json");
        Directory.CreateDirectory(Path.GetDirectoryName(apps)!);
        File.WriteAllText(apps, """{ "github.com:z": { "oauth_token": "z-token" }, "github.com:a": { "oauth_token": "a-token" }, "other": { "oauth_token": "x" } }""");
        try
        {
            Assert.Equal("a-token", new CopilotLimits().Credential(_home)!.Token);
            File.WriteAllText(apps, """{ "other": { "oauth_token": "x" } }""");
            Assert.Null(new CopilotLimits().Credential(_home));
        }
        finally
        {
            File.Delete(apps);
        }
    }

    [Fact]
    public async Task Codex_reads_its_token_plan_and_account_and_sends_them()
    {
        Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(_home, "cx"));
        var p = new CodexLimits();
        Assert.Null(p.Credential(_home));
        Write("cx/auth.json", """{ "OPENAI_API_KEY": "sk-x" }""");
        Assert.Null(p.Credential(_home));   // an API key has no plan limits

        var id = Jwt("""{ "https://api.openai.com/auth": { "chatgpt_plan_type": "plus" } }""");
        var access = Jwt("""{ "exp": 4102444800 }""");
        Write("cx/auth.json", $$"""{ "tokens": { "access_token": "{{access}}", "id_token": "{{id}}", "account_id": "acc-1" } }""");
        var c = p.Credential(_home)!;
        Assert.Equal("Plus", c.Plan);
        Assert.Equal(new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc), c.Expires);

        var http = new StubHandler(_ => StubHandler.Json("""{ "rate_limit": { "primary_window": { "used_percent": 20, "limit_window_seconds": 18000 } } }"""));
        var snap = await p.Fetch(new HttpClient(http), c, TestContext.Current.CancellationToken);
        Assert.Equal("acc-1", http.Requests.Single().Headers.GetValues("ChatGPT-Account-Id").Single());
        Assert.Single(snap.Windows);
    }

    [Fact]
    public void Codex_without_logs_has_no_figures_from_them()
    {
        var def = new AiStackprint.Models.AppDefinition
        {
            Id = "codex-cli",
            Name = "Codex CLI"
        };
        Assert.Null(new CodexLimits().FromLogs(def, []));
    }

    [Fact]
    public async Task Gemini_reads_its_oauth_file_and_asks_for_the_plan_then_the_quota()
    {
        var p = new GeminiLimits();
        Assert.Null(p.Credential(_home));
        Write(".gemini/oauth_creds.json", """{ "access_token": "g-token", "expiry_date": 4102444800000 }""");
        var c = p.Credential(_home)!;
        Assert.Equal("g-token", c.Token);
        Assert.Null(c.Extra);
        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", "proj-1");
        Assert.Equal("proj-1", p.Credential(_home)!.Extra!["project"]);
        Environment.SetEnvironmentVariable("GOOGLE_CLOUD_PROJECT", null);

        var withProject = new StubHandler(r => r.RequestUri!.AbsoluteUri.EndsWith("loadCodeAssist", StringComparison.Ordinal)
            ? StubHandler.Json("""{ "currentTier": { "name": "Standard" }, "cloudaicompanionProject": "found-proj" }""")
            : StubHandler.Json("""{ "buckets": [ { "modelId": "gemini-2.5-pro", "remainingFraction": 0.5 } ] }"""));
        var snap = await p.Fetch(new HttpClient(withProject), c, TestContext.Current.CancellationToken);
        Assert.Equal(("Standard", 2), (snap.Plan, withProject.Requests.Count));
        Assert.Contains("found-proj", withProject.Bodies[1]);

        var noProject = new StubHandler(_ => StubHandler.Json("""{ "currentTier": { "id": "free-tier" } }"""));
        var none = await p.Fetch(new HttpClient(noProject), c, TestContext.Current.CancellationToken);
        Assert.Equal("No Google Cloud project for quota.", none.Note);
        Assert.Single(noProject.Requests);
    }

    [Fact]
    public async Task A_service_that_refuses_raises_the_error_with_its_retry_time()
    {
        var c = new Credential("t", null, "file", null);
        var refuse = new StubHandler(_ =>
        {
            var r = StubHandler.Json("{}", HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return r;
        });
        var e = await Assert.ThrowsAsync<LimitHttpException>(() => new CodexLimits().Fetch(new HttpClient(refuse), c, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.TooManyRequests, e.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(90), e.RetryAfter);

        var later = new StubHandler(_ =>
        {
            var r = StubHandler.Json("{}", HttpStatusCode.Unauthorized);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(5));
            return r;
        });
        var e2 = await Assert.ThrowsAsync<LimitHttpException>(() => new CodexLimits().Fetch(new HttpClient(later), c, TestContext.Current.CancellationToken));
        Assert.True(e2.RetryAfter > TimeSpan.FromMinutes(4));
    }

    [Fact]
    public void The_jwt_helpers_read_claims_and_expiry_and_ignore_a_token_that_is_not_one()
    {
        var t = Jwt("""{ "exp": 4102444800, "sub": "auth0|user-9" }""");
        Assert.Equal("auth0|user-9", LimitProviders.JwtClaims(t)!.Value.GetProperty("sub").GetString());
        Assert.Equal(new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc), LimitProviders.JwtExpiry(t));
        Assert.Null(LimitProviders.JwtClaims("not-a-jwt"));
        Assert.Null(LimitProviders.JwtClaims("a.b.c"));
        Assert.Null(LimitProviders.JwtExpiry("x"));
        Assert.Equal("Free tier", LimitProviders.Cap("free_tier"));
        Assert.Equal("", LimitProviders.Cap(""));
        Assert.Same(LimitProviders.All.First(x => x.Id == "gemini"), LimitProviders.Find("gemini"));
        Assert.Null(LimitProviders.Find("nobody"));
        Assert.Null(LimitProviders.Find(null));
    }

    [Fact]
    public async Task Cursor_reads_the_token_user_and_plan_from_its_database_and_sends_a_cookie()
    {
        var db = Path.Combine(_home, "state.vscdb");
        CursorLimits.DbOverride = db;
        var p = new CursorLimits();
        Assert.Null(p.Credential(_home));   // no database

        var token = Jwt("""{ "exp": 4102444800, "sub": "google-oauth2|user_42" }""");
        using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE ItemTable (key TEXT, value TEXT)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO ItemTable VALUES ('other', 'x')";
            cmd.ExecuteNonQuery();
        }

        Assert.Null(p.Credential(_home));   // a database without a token

        using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"INSERT INTO ItemTable VALUES ('cursorAuth/accessToken', '\"{token}\"'), ('cursorAuth/stripeMembershipType', 'pro_plus')";
            cmd.ExecuteNonQuery();
        }

        var c = p.Credential(_home)!;
        Assert.Equal(token, c.Token);
        Assert.Equal("Pro plus", c.Plan);
        Assert.Equal("user_42", c.Extra!["user"]);

        var http = new StubHandler(_ => StubHandler.Json("""{ "membershipType": "pro", "individualUsage": { "plan": { "used": 500, "limit": 2000 } } }"""));
        var snap = await p.Fetch(new HttpClient(http), c, TestContext.Current.CancellationToken);
        Assert.Equal($"WorkosCursorSessionToken=user_42%3A%3A{token}", http.Requests.Single().Headers.GetValues("Cookie").Single());
        Assert.Single(snap.Windows);
    }
}
