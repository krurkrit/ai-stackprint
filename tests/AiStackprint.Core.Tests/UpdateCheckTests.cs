// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class UpdateCheckTests
{
    private const string Page = "https://github.com/krurkrit/ai-stackprint/releases/tag/";

    private static string Reply(string tag, bool draft = false, bool pre = false, string? url = null) =>
        $$"""{ "tag_name": "{{tag}}", "html_url": "{{url ?? Page + tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(pre ? "true" : "false")}} }""";

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V0.10.0", "0.10.0")]
    [InlineData("v2.0", "2.0.0")]
    [InlineData(" v1.2.3 ", "1.2.3")]
    public void Tags_parse_as_versions(string tag, string expected) => Assert.Equal(Version.Parse(expected), UpdateCheck.ParseTag(tag));

    [Theory]
    [InlineData("v1.2.3-beta")]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_tags_are_ignored(string? tag) => Assert.Null(UpdateCheck.ParseTag(tag));

    [Fact]
    public void Release_reply_gives_version_and_page()
    {
        var r = UpdateCheck.Parse(Reply("v0.2.0"));
        Assert.NotNull(r);
        Assert.Equal(new Version(0, 2, 0), r.Version);
        Assert.Equal(Page + "v0.2.0", r.Url);
    }

    [Fact]
    public void Drafts_prereleases_and_other_pages_are_skipped()
    {
        Assert.Null(UpdateCheck.Parse(Reply("v0.2.0", draft: true)));
        Assert.Null(UpdateCheck.Parse(Reply("v0.2.0", pre: true)));
        Assert.Null(UpdateCheck.Parse(Reply("v0.2.0", url: "https://example.com/ai-stackprint/v0.2.0")));
        Assert.Null(UpdateCheck.Parse(Reply("v0.2.0", url: "https://github.com/someone-else/ai-stackprint/releases/tag/v0.2.0")));
        Assert.Null(UpdateCheck.Parse("[]"));
    }

    [Theory]
    [InlineData("0.1.0", "v0.1.1", true)]
    [InlineData("0.1.0", "v0.2.0", true)]
    [InlineData("0.9.9", "v1.0.0", true)]
    [InlineData("0.1.0", "v0.1.0", false)]
    [InlineData("0.2.0", "v0.1.9", false)]
    public void Only_a_higher_version_is_newer(string current, string tag, bool newer)
    {
        // The build's version has four parts (0.1.0.0); the tag has three
        var cur = new Version(current + ".0");
        Assert.Equal(newer, UpdateCheck.Newer(UpdateCheck.Parse(Reply(tag)), cur) != null);
    }

    [Theory]
    [InlineData("0.2.0+6f1591a0c2", "0.2.0")]
    [InlineData("0.2.1-alpha.0.3+0fdb0f7", "0.2.1-alpha.0.3")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData(null, "0.0.0")]
    [InlineData("", "0.0.0")]
    public void Label_drops_the_commit(string? informational, string expected) => Assert.Equal(expected, UpdateCheck.LabelOf(informational));

    [Fact]
    public void No_release_is_never_newer() => Assert.Null(UpdateCheck.Newer(null, new Version(0, 1, 0)));

    [Fact]
    public void Automatic_check_runs_once_a_day()
    {
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateCheck.Due(null, now));
        Assert.False(UpdateCheck.Due(now.AddHours(-23), now));
        Assert.True(UpdateCheck.Due(now.AddHours(-24), now));
        Assert.True(UpdateCheck.Due(now.AddDays(2), now));   // clock moved back
    }
}
