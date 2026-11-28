// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal static partial class Dialogs
{
    /// <summary>What to export: parts and only changes or all values. Null on cancel.</summary>
    internal static Window ExportChoice(Action<(DataParts Parts, bool All)?> done)
    {
        Window? w = null;
        var parts = DataParts.All;
        var all = false;
        CheckBox Part(string label, DataParts p) => Check(label, true, on => parts = on ? parts | p : parts & ~p);
        var go = Btn("Export…", () =>
        {
            if (parts != DataParts.None)
            {
                done((parts, all));
                w!.Close(true);
            }
        }, true).With(b => b.IsDefault = true);
        var body = new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                Txt("One file to move your setup to another computer or keep as a copy. No state, caches, logs or secrets; paths under your home folder are written as ~/.", TextSize.Small, false, Muted),
                new WrapPanel
                {
                    ItemSpacing = Space.XL,
                    Children = { Part("Settings", DataParts.Settings), Part("Models", DataParts.Models), Part("Apps", DataParts.Apps), Part("Projects", DataParts.Projects) }
                },
                Radios([("Only my changes", "c"), ("All values", "a")], "c", v => all = v == "a"),
                Txt("Only my changes: what you changed from the defaults and the model data. All values: every setting and every model, as this version has them.", TextSize.Caption, false, Muted),
            }
        };
        w = Shell("Export", body, Btn("Cancel", () =>
        {
            done(null);
            w!.Close(false);
        }).With(b => b.IsCancel = true), go);
        return w;
    }

    /// <summary>
    /// The import preview: every change, what is left out, and missing project folders to locate or skip. <paramref name="done"/> gets the chosen new
    /// folders (old path → new path) on Import, null on Cancel.
    /// </summary>
    internal static Window ImportPreview(ImportPlan plan, Action<Dictionary<string, string>?> done)
    {
        Window? w = null;
        var moved = new Dictionary<string, string>();
        var body = new StackPanel { Spacing = Space.L };
        body.Children.Add(Txt(plan.AllValues
            ? "This file holds every value, not only changes. Each one goes over yours; values equal to this version's defaults or model data are not saved as your changes."
            : "These values go over yours; everything else stays as it is. Each file is copied to backups/ first.", TextSize.Small, false, Muted));
        if (plan.Changes.Count > 0)
        {
            var rows = plan.Changes.Select(c => new TableRow([Txt(c.Part.ToString(), TextSize.Small, false, Muted), Txt(c.What, TextSize.Small, true), Txt(c.Before ?? "–", TextSize.Small, false, Muted), Txt(c.After ?? "–", TextSize.Small)]));
            body.Children.Add(new ScrollViewer
            {
                MaxHeight = Heights.ViewerS,
                Content = Table.Build([new Col("Part", ".8*"), new Col("What", "2*"), new Col("Now", "1.2*"), new Col("Imported", "1.2*")], rows),
            });
        }

        foreach (var missing in plan.MissingProjects)
        {
            var state = Txt("Skipped", TextSize.Small, false, Muted);
            var locate = Btn("Locate…", async () =>
            {
                var r = await w!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = $"Where is {Ctx.ProjectName(missing)}?" });
                if (r.Count > 0 && r[0].TryGetLocalPath() is { } p)
                {
                    moved[missing] = p;
                    state.Text = "→ " + p;
                }
            }, false, "folder_open");
            body.Children.Add(Line("warning", WarningMark, Txt($"Project not found here: {missing}", TextSize.Small), locate, state));
        }

        foreach (var n in plan.Notes)
        {
            body.Children.Add(Line("info", Muted, Txt(n, TextSize.Small, false, Muted)));
        }

        var go = Btn("Import (backup first)", () =>
        {
            done(moved);
            w!.Close(true);
        }, true);
        w = Shell("Import", body, Btn("Cancel", () =>
        {
            done(null);
            w!.Close(false);
        }).With(b => b.IsCancel = true), go);
        w.Width = OuterWidth(Widths.DialogL);
        return w;
    }

    /// <summary>An icon, text that wraps in the space left, then any controls at the end.</summary>
    private static Grid Line(string icon, string key, TextBlock text, params Control[] end)
    {
        var g = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*" + string.Concat(end.Select(_ => ",Auto"))),
            ColumnSpacing = Space.M
        };
        g.Children.Add(Icon(icon, IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, key).With(i => i.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center));
        text.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        for (var i = 0; i < end.Length; i++)
        {
            end[i].VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Grid.SetColumn(end[i], i + 2);
            g.Children.Add(end[i]);
        }

        return g;
    }
}
