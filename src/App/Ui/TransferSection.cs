// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Settings › Export and import (<see cref="DataExport"/>, <see cref="DataImport"/>): a file of your settings, models, apps and projects.</summary>
internal static class TransferSection
{
    public static Control Build(Ctx x) =>
        Section("arrow_sync", "Export and import", new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                Txt("Export your settings, model changes, custom apps and project folders to one file; import it here or on another computer. An import shows every change first and backs up your files.", TextSize.Small, false, Muted),
                Row(Btn("Export…", () => Export(x), false, "save"), Btn("Import…", () => Import(x), false, "folder_open")),
            }
        });

    private static async void Export(Ctx x)
    {
        (DataParts Parts, bool All)? choice = null;
        await Dialogs.ExportChoice(c => choice = c).ShowDialog<bool>(x.Owner);
        if (choice is not { } c)
        {
            return;
        }

        var file = await x.Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export",
            SuggestedFileName = DataExport.FileName,
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            DataExport.Write(path, DataExport.Build(c.Parts, c.All, DataFiles.Mine, DateOnly.FromDateTime(DateTime.Today)));
            Toast.Show("Exported to " + path, "save", 4);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Toast.Show("Could not write the export: " + e.Message, "warning", 8); }
    }

    private static async void Import(Ctx x)
    {
        var files = await x.Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import", FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        ImportPlan plan;
        try
        {
            plan = DataImport.Plan(await File.ReadAllTextAsync(path), DataFiles.Mine);
        }
        catch (ImportRefusedException e) { await Dialogs.Message(x.Owner, "Cannot import", e.Message, warn: true); return; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await Dialogs.Message(x.Owner, "Cannot import", e.Message, warn: true); return; }

        if (plan.Changes.Count == 0 && plan.MissingProjects.Count == 0)
        {
            await Dialogs.Message(x.Owner, "Nothing to import", string.Join("\n", ["This file holds nothing that differs from what you have.", .. plan.Notes]));
            return;
        }

        Dictionary<string, string>? moved = null;
        await Dialogs.ImportPreview(plan, m => moved = m).ShowDialog<bool>(x.Owner);
        if (moved == null)
        {
            return;
        }

        try
        {
            var backup = DataImport.Apply(plan, DataFiles.Mine, moved, DateTime.Now);
            Toast.Show(backup == null ? "Nothing was changed." : "Imported. Your files before it are in " + backup, "info", 6);
            x.Reload();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { Toast.Show("Import stopped: " + e.Message, "warning", 8); }
    }
}
