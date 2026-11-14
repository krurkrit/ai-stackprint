// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public class PluginManagerTests
{
    private static readonly string[] _template = ["claude", "plugin", "install", "{plugin}", "--scope", "{scope}"];

    [Fact]
    public void Build_fills_tokens()
    {
        Assert.Equal(new[] { "claude", "plugin", "install", "fmt@market", "--scope", "project" }, PluginManager.Build(_template, "fmt@market", "project"));
    }

    [Theory]
    [InlineData("fmt; rm -rf /")]
    [InlineData("--help")]
    [InlineData("a b")]
    [InlineData("")]
    public void Build_rejects_unsafe_plugin_ids(string id) =>
        Assert.Throws<ArgumentException>(() => PluginManager.Build(_template, id, "user"));

    [Fact]
    public void Build_rejects_unknown_scope_and_empty_template()
    {
        Assert.Throws<ArgumentException>(() => PluginManager.Build(_template, "fmt", "managed"));
        Assert.Throws<InvalidOperationException>(() => PluginManager.Build([], "fmt", "user"));
    }
}
