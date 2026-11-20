// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class ModelDataUpdateTests : IDisposable
{
    private const string Good = """{ "version": 1, "updated": "2026-11-22", "models": { "claude-opus-5-5": { "provider": "anthropic", "price": { "input": 4, "output": 20 } } } }""";
    private readonly string _dir = Directory.CreateTempSubdirectory("modeldata-").FullName;
    private string Cache => Path.Combine(_dir, "cache", "models.json");

    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>Answers every request with <paramref name="answer"/> and keeps the request it got.</summary>
    private sealed class Fake(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return answer(request, cancellationToken);
        }
    }

    private static Fake Reply(HttpStatusCode code, string body = "", string? etag = null) => new((_, _) =>
    {
        var r = new HttpResponseMessage(code) { Content = new StringContent(body) };
        if (etag != null)
        {
            r.Headers.ETag = new EntityTagHeaderValue(etag);
        }
        return Task.FromResult(r);
    });

    private Task<ModelDataResult> Run(Fake fake, string? etag = null, TimeSpan? timeout = null) =>
        ModelDataUpdate.RunAsync(new HttpClient(fake) { Timeout = timeout ?? TimeSpan.FromSeconds(10) }, Cache, etag, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Valid_data_is_saved_with_its_etag_and_the_request_asks_only_for_the_file()
    {
        var fake = Reply(HttpStatusCode.OK, Good, "\"abc\"");
        var r = await Run(fake);
        Assert.True(r.Saved && r.Ok);
        Assert.Equal("\"abc\"", r.Etag);
        Assert.Equal(Good, File.ReadAllText(Cache));
        Assert.Equal("https://api.github.com/repos/krurkrit/ai-stackprint/contents/default/models.json", fake.Last!.RequestUri!.ToString());
        Assert.Contains("application/vnd.github.raw+json", fake.Last.Headers.Accept.ToString(), StringComparison.Ordinal);
        Assert.Null(fake.Last.Headers.Authorization);
        Assert.False(fake.Last.Headers.Contains("If-None-Match"));   // no copy yet
        Assert.Equal([], Directory.GetFiles(Path.GetDirectoryName(Cache)!, "*.tmp"));
    }

    [Fact]
    public async Task Not_modified_keeps_the_copy_and_sends_the_etag_only_when_the_copy_exists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Cache)!);
        File.WriteAllText(Cache, Good);
        var fake = Reply(HttpStatusCode.NotModified);
        var r = await Run(fake, "\"abc\"");
        Assert.True(r.Ok);
        Assert.False(r.Saved);
        Assert.Equal("\"abc\"", fake.Last!.Headers.GetValues("If-None-Match").Single());
        Assert.Equal(Good, File.ReadAllText(Cache));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task An_error_status_keeps_the_last_good_copy(HttpStatusCode code, string shown)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Cache)!);
        File.WriteAllText(Cache, Good);
        var r = await Run(Reply(code, "{}"));
        Assert.False(r.Ok);
        Assert.Contains(shown, r.Message, StringComparison.Ordinal);
        Assert.Equal(Good, File.ReadAllText(Cache));
    }

    [Theory]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("""{ "version": 2, "updated": "2027-01-01", "models": { "x": { "provider": "openai" } } }""", "newer AI Stackprint")]
    [InlineData("""{ "version": 1, "updated": "2026-11-22", "models": {} }""", "expected form")]
    [InlineData("""{ "version": 1, "updated": "soon", "models": { "x": { "provider": "openai" } } }""", "expected form")]
    [InlineData("""{ "version": 1, "updated": "2026-11-22", "models": { "x": { "price": {} } } }""", "expected form")]
    public async Task Data_that_is_not_usable_is_not_saved(string body, string shown)
    {
        var r = await Run(Reply(HttpStatusCode.OK, body));
        Assert.False(r.Saved || r.Ok);
        Assert.Contains(shown, r.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Cache));
    }

    [Fact]
    public async Task A_timeout_is_a_failure_not_an_exception()
    {
        var slow = new Fake(async (_, ct) =>
        {
            await Task.Delay(5000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var r = await Run(slow, timeout: TimeSpan.FromMilliseconds(50));
        Assert.False(r.Ok);
        Assert.Contains("in time", r.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_network_is_a_failure_not_an_exception()
    {
        var r = await Run(new Fake((_, _) => throw new HttpRequestException("No such host is known.")));
        Assert.False(r.Ok);
        Assert.Contains("No such host", r.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_saved_copy_is_used_when_it_is_newer_than_the_built_in_data()
    {
        await Run(Reply(HttpStatusCode.OK, Good));
        var builtIn = System.Text.Json.Nodes.JsonNode.Parse("""{ "version": 1, "updated": "2026-11-15", "models": { "claude-opus-5-5": { "provider": "anthropic", "price": { "input": 9, "output": 9 } } } }""")!.AsObject();
        var t = ModelCatalog.Build(builtIn, ModelCatalog.ReadCache(Cache), null);
        Assert.True(t.Downloaded);
        Assert.Equal(4, ModelCatalog.For("claude-opus-5-5", t)!.Price!.Input);
    }

    [Theory]
    [InlineData("2026-09-01", "2026-11-15", true)]
    [InlineData("2026-10-01", "2026-11-15", false)]
    [InlineData(null, "2026-11-15", false)]
    public void Data_over_60_days_old_is_stale(string? updated, string now, bool stale) =>
        Assert.Equal(stale, ModelDataUpdate.Stale(updated, DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));
}
