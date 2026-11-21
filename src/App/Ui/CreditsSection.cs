// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Settings › Credits: the free and open-source projects the app uses, with license and link (default/credits.json, <see cref="Credits"/>).</summary>
internal static class CreditsSection
{
    public static Control Build(Ctx x)
    {
        var rows = Credits.All().Select(c => new TableRow(
        [
            Beside(LogoOr(c.Logo, GroupIcon(c.Group), Sizes.LogoM), Txt(c.Name, TextSize.Body, true).With(t => t.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center)),
            Txt(c.Group, TextSize.Small, false, Muted),
            Txt(c.License, TextSize.Small),
            Txt(c.Use, TextSize.Small, false, Muted),
            IconBtn("open", "Open " + c.Url, () => Open(x, c.Url))
        ], () => Open(x, c.Url)));
        var table = Table.Build([Cols.Name("Name", "1.4*"), Cols.Type("Group"), Cols.Type("License"), new Col("Used for", "3*"), Cols.Action()], rows);
        return Section("info", "Credits", new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                Txt($"AI Stackprint {UpdateCheck.Label} is free software under the MIT License. It stands on the free and open-source projects below; thank you to their authors. " +
                    "Fonts are not bundled: they are used when installed. Logos belong to their owners and only identify the apps.", TextSize.Small, false, Muted),
                table
            }
        });
    }

    /// <summary>Icon of a row without a logo.</summary>
    private static string GroupIcon(string group) => group switch
    {
        "Framework" => "puzzle_piece",
        "Editor" => "code",
        "Icons" => "sparkle",
        "Data" => "data_pie",
        "Fonts" => "text_font",
        _ => "wrench"
    };

    private static void Open(Ctx x, string url) => Fire.Forget(OpenAsync(x, url));

    private static async Task OpenAsync(Ctx x, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            await x.Owner.Launcher.LaunchUriAsync(uri);
        }
    }
}
