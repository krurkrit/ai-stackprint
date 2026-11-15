// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class TransferDialogTests
{
    private static ImportPlan Plan(bool all) => new(
        [], all,
        [new(DataParts.Settings, "theme", "System", "Dark"), new(DataParts.Models, "claude-opus-5-5 · price.input", "4", "5")],
        ["/gone/demo"],
        ["Settings: checkUpdates is not imported (network calls stay as they are here; turn them on in Settings)."]);

    [AvaloniaFact]
    public void The_preview_lists_changes_missing_projects_and_notes_and_returns_on_import()
    {
        Dictionary<string, string>? moved = null;
        var w = Dialogs.ImportPreview(Plan(false), m => moved = m);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var texts = Ui.Texts(w);
        Assert.Contains("theme", texts);
        Assert.Contains("Dark", texts);
        Assert.Contains("claude-opus-5-5 · price.input", texts);
        Assert.Contains("Project not found here: /gone/demo", texts);
        Assert.Contains("Skipped", texts);
        Assert.Contains(texts, t => t.Contains("checkUpdates", StringComparison.Ordinal));

        Ui.Click(Ui.Button(w, "Import (backup first)"));
        Assert.NotNull(moved);
        Assert.Empty(moved);
    }

    [AvaloniaFact]
    public void All_values_are_explained_and_cancel_returns_null()
    {
        var called = false;
        Dictionary<string, string>? moved = [];
        var w = Dialogs.ImportPreview(Plan(true), m => { called = true; moved = m; });
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Ui.Texts(w), t => t.StartsWith("This file holds every value", StringComparison.Ordinal));
        Ui.Click(Ui.Button(w, "Cancel"));
        Assert.True(called);
        Assert.Null(moved);
    }

    [AvaloniaFact]
    public void Export_choice_returns_the_parts_left_on()
    {
        (DataParts Parts, bool All)? choice = null;
        var w = Dialogs.ExportChoice(c => choice = c);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Ui.Click(Ui.Button(w, "Export…"));
        Assert.Equal((DataParts.All, false), choice);
    }
}
