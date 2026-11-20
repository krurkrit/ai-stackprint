// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>One free or open-source project the app uses (a row of default/credits.json).</summary>
/// <param name="Name">Shown name.</param>
/// <param name="Group">Framework, Editor, Icons, Design, Data, Fonts or Build.</param>
/// <param name="License">SPDX id or the license's name.</param>
/// <param name="Url">Home page or repository (https).</param>
/// <param name="Use">What the app uses it for.</param>
/// <param name="Packages">NuGet ids the entry covers (a test checks every package of the solution is covered).</param>
/// <param name="Shipped">False for tools used to build, test and release only, and fonts that are not bundled.</param>
/// <param name="Logo">Key in the app's logos.json, or null: the row then shows its group's icon.</param>
public record Credit(string Name, string Group, string License, string Url, string Use, List<string> Packages, bool Shipped, string? Logo = null);

/// <summary>The credits list: the built-in default/credits.json, sorted A to Z by name.</summary>
public static class Credits
{
    private static readonly JsonSerializerOptions _opt = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed record Doc(List<Credit> Credits);

    public static List<Credit> All()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("default/credits.json")!;
        var doc = JsonSerializer.Deserialize<Doc>(s, _opt)!;
        return [.. doc.Credits.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
