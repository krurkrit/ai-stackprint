// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Headless.XUnit;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class CreditsTests
{
    [AvaloniaFact]
    public void Every_logo_a_credit_names_exists_in_logos_json()
    {
        var missing = Credits.All().Where(c => c.Logo != null && Icons.Logo(c.Logo) == null).Select(c => c.Name).ToList();
        Assert.True(missing.Count == 0, "No logo for: " + string.Join(", ", missing));
    }
}
