using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class PluginManagerTests
{
    static readonly string[] Template = { "claude", "plugin", "install", "{plugin}", "--scope", "{scope}" };

    [Fact]
    public void Build_fills_tokens()
    {
        Assert.Equal(new[] { "claude", "plugin", "install", "fmt@market", "--scope", "project" }, PluginManager.Build(Template, "fmt@market", "project"));
    }

    [Theory]
    [InlineData("fmt; rm -rf /")]
    [InlineData("--help")]
    [InlineData("a b")]
    [InlineData("")]
    public void Build_rejects_unsafe_plugin_ids(string id) =>
        Assert.Throws<ArgumentException>(() => PluginManager.Build(Template, id, "user"));

    [Fact]
    public void Build_rejects_unknown_scope_and_empty_template()
    {
        Assert.Throws<ArgumentException>(() => PluginManager.Build(Template, "fmt", "managed"));
        Assert.Throws<InvalidOperationException>(() => PluginManager.Build(Array.Empty<string>(), "fmt", "user"));
    }
}
