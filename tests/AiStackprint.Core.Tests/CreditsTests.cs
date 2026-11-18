// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Xml.Linq;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class CreditsTests
{
    [Fact]
    public void Every_credit_has_a_name_license_https_link_and_use_and_names_are_unique()
    {
        var all = Credits.All();
        Assert.NotEmpty(all);
        Assert.All(all, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.License));
            Assert.False(string.IsNullOrWhiteSpace(c.Use));
            Assert.StartsWith("https://", c.Url, StringComparison.Ordinal);
        });
        Assert.Equal(all.Count, all.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_list_is_sorted_A_to_Z()
    {
        var names = Credits.All().Select(c => c.Name).ToList();
        Assert.Equal([.. names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)], names);
    }

    [Fact]
    public void Every_package_of_the_solution_is_credited()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var props = XDocument.Load(Path.Combine(dir.FullName, "Directory.Packages.props"));
        var packages = props.Descendants().Where(e => e.Name.LocalName is "PackageVersion" or "GlobalPackageReference")
            .Select(e => (string?)e.Attribute("Include")).OfType<string>().ToList();
        var credited = Credits.All().SelectMany(c => c.Packages).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = packages.Where(p => !credited.Contains(p)).ToList();
        Assert.True(missing.Count == 0, "Not in default/credits.json: " + string.Join(", ", missing));
    }
}
