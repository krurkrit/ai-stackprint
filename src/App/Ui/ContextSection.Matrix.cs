// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class ContextSection
{
    private bool _byFile;   // "By file" pivot instead of one table per app

    /// <summary>One row per file, one column per app: how that app loads the file. Built from the reports already read.</summary>
    private Control Matrix(List<(AppDefinitionRef App, ContextReport Report)> reports)
    {
        var apps = reports.Where(r => r.Report.Files.Count > 0).ToList();
        var files = apps.SelectMany(r => r.Report.Files.Select(f => f.Path)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ShortPath, StringComparer.OrdinalIgnoreCase).ToList();
        var rows = files.Select(p => new TableRow(
        [
            Txt(ShortPath(p), TextSize.Small, true).With(t => t.FontFamily = Theme.Mono),
            .. apps.Select(a => a.Report.Files.FirstOrDefault(f => string.Equals(f.Path, p, StringComparison.OrdinalIgnoreCase)) is { } f
                ? Txt(Short(f.Load), TextSize.Small, false, f.Load == LoadWhen.Ignored ? Muted : Text)
                : Txt("", TextSize.Small))
        ]));
        var cols = new[] { new Col("File", "3*") }.Concat(apps.Select(a => new Col(a.App.Name, "1*", false, 90))).ToArray();
        return Table.Build(cols, rows);
    }

    private static string Short(LoadWhen w) => w switch
    {
        LoadWhen.Always => "Launch",
        LoadWhen.Imported => "Import",
        LoadWhen.OnDemand => "On demand",
        LoadWhen.Conditional => "Matching",
        LoadWhen.AgentDecides => "Agent",
        LoadWhen.Manual => "Mention",
        _ => "Ignored"
    };
}
