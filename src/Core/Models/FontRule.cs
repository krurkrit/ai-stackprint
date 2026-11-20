// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace AiStackprint.Models;

/// <summary>How one field of a font setting gets its value.</summary>
[JsonConverter(typeof(FontModeConverter))]
public enum FontMode
{
    /// <summary>The setting's own value.</summary>
    Set,
    /// <summary>The parent's resolved value ("Use Body").</summary>
    Link,
    /// <summary>The parent's resolved value plus an offset (size and weight only).</summary>
    Adjust
}

/// <summary>Writes the mode in camel case ("set", "link", "adjust"), as default/settings.json has it.</summary>
internal sealed class FontModeConverter : JsonStringEnumConverter<FontMode>
{
    public FontModeConverter() : base(System.Text.Json.JsonNamingPolicy.CamelCase) { }
}

/// <summary>Allowed values of a size or weight: <c>min</c>/<c>max</c>/<c>step</c>, or a list of <c>options</c>. Empty parts use the role's limits, then the built-in ones.</summary>
public sealed class FontLimit
{
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public double[]? Options { get; set; }
}

/// <summary>The family of a font setting: its own name, or the parent's.</summary>
public sealed class FontFamilyRule
{
    /// <summary>Empty: by default own value without a parent, linked with one (see <see cref="FontRule.Of(FontFamilyRule)"/>).</summary>
    public FontMode? Mode { get; set; }
    public string? Value { get; set; }
}

/// <summary>The size (px) or weight of a font setting: its own value, the parent's, or the parent's plus <see cref="Value"/>.</summary>
public sealed class FontNumberRule
{
    /// <summary>Empty: by default own value without a parent, linked with one (see <see cref="FontRule.Of(FontNumberRule)"/>).</summary>
    public FontMode? Mode { get; set; }
    public double? Value { get; set; }
    public FontLimit? Limit { get; set; }
}

/// <summary>
/// One font setting (Body, Headings, …): family, size and weight, optionally linked to a <see cref="Parent"/> setting.
/// Labels are not stored. A field with no mode is <see cref="FontMode.Set"/> when there is no parent and <see cref="FontMode.Link"/> when there is one, so only a different mode is written.
/// <see cref="Parent"/> is empty for none; it is written only when it differs from the default (then an empty one turns a default parent off).
/// </summary>
public sealed class FontRule
{
    public string Parent { get; set; } = "";
    public FontFamilyRule Family { get; set; } = new();
    public FontNumberRule Size { get; set; } = new();
    public FontNumberRule Weight { get; set; } = new();

    /// <summary>How a field gets its value: its mode, or the default one for this setting.</summary>
    public FontMode Of(FontFamilyRule field) => field.Mode ?? DefaultMode();

    public FontMode Of(FontNumberRule field) => field.Mode ?? DefaultMode();

    private FontMode DefaultMode() => Parent.Length > 0 ? FontMode.Link : FontMode.Set;
}

/// <summary>The look of a font setting after its links and offsets are worked out and fitted to its own limits.</summary>
public readonly record struct ResolvedFont(string Family, double Size, int Weight, double RawSize, double RawWeight);
