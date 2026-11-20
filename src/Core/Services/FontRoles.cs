// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>One font setting the app has: its label, which of family, size and weight it uses, and the limits the app allows. Limits live here, not in saved settings.</summary>
public sealed record FontRole(string Key, string Label, FontLimit Size, FontLimit Weight, bool Mono, string DefaultFamily, string Note);

/// <summary>The font settings of the app (Settings › Appearance); the keys are the keys of <see cref="Settings.Fonts"/>.</summary>
public static class FontRoles
{
    public const string Body = "body", Emphasis = "emphasis", Heading = "heading", Menu = "menu", Log = "log";

    private static readonly FontLimit _text = new() { Min = 12, Max = 18, Step = 1 };
    private static readonly FontLimit _code = new() { Min = 10, Max = 18, Step = 1 };
    private static readonly FontLimit _weights = new() { Min = 300, Max = 700, Step = 100 };

    public static IReadOnlyList<FontRole> All { get; } =
    [
        new(Body, "Body", _text, _weights, false, "Inter", "Text everywhere; every size scales with it"),
        new(Emphasis, "Emphasis", _text, _weights, false, "Inter", "Names and labels in rows and tables; size and family follow Body unless changed"),
        new(Heading, "Headings", _text, _weights, false, "Inter", "Page, card and section titles; their size steps scale with this size over Body"),
        new(Menu, "Side menu", _text, _weights, false, "Inter", "Side menu items"),
        new(Log, "Code & logs", _code, _weights, true, "JetBrains Mono", "Logs and the config editor"),
    ];

    public static FontRole? Find(string key) => All.FirstOrDefault(r => r.Key == key);

    public static string Label(string key) => Find(key)?.Label ?? key;
}

/// <summary>Names of the standard weights, as shown in the lists: <c>Normal (400)</c>, <c>Bold (700)</c>.</summary>
public static class FontWeights
{
    public static string Label(double weight)
    {
        var w = (int)Math.Round(weight);
        var name = w switch
        {
            100 => "Thin",
            200 => "ExtraLight",
            300 => "Light",
            400 => "Normal",
            500 => "Medium",
            600 => "SemiBold",
            700 => "Bold",
            800 => "ExtraBold",
            900 => "Black",
            _ => "Weight"
        };
        return $"{name} ({w})";
    }
}
