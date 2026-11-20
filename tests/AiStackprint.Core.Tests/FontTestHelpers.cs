// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

namespace AiStackprint.Core.Tests;

/// <summary>Short ways to read and change the font settings in tests.</summary>
internal static class FontTestHelpers
{
    public static ResolvedFont Font(this Settings s, string? key = null) => new FontRuleSet(s.Fonts).Resolve(key ?? FontRoles.Body);

    public static void SetSize(this Settings s, double size, string? key = null) => new FontRuleSet(s.Fonts).SetNumber(key ?? FontRoles.Body, FontProp.Size, size);

    /// <summary>Gives the setting its own weight (leaving link or adjust), as a user typing a value would.</summary>
    public static void SetWeight(this Settings s, int weight, string key)
    {
        var set = new FontRuleSet(s.Fonts);
        set.SetMode(key, FontProp.Weight, FontMode.Set);
        set.SetNumber(key, FontProp.Weight, weight);
    }
}
