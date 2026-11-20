// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;
using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AiStackprint.App.Tests;

public sealed partial class ScreenshotTests
{
    [AvaloniaFact]
    public void Features_and_dialogs()
    {
        Assert.SkipUnless(_out != null, "Set STACKPRINT_SHOTS to a folder to write screenshots.");
        var data = JsonNode.Parse("""
            {
              "claude-opus-5-5": { "provider": "anthropic", "contextWindow": 1000000, "maxOutput": 128000, "retires": "2027-02-01", "price": { "input": 4, "output": 20 } },
              "gpt-6": { "provider": "openai", "contextWindow": 400000, "price": { "input": 2, "output": 8 } }
            }
            """)!.AsObject();
        var file = Path.Combine(_tmp, "models.json");
        var edits = new ModelEdits(() => ModelCatalog.Rows(data, file), (k, v) => ModelCatalog.Save(data, file, k, v), (k, f) => ModelCatalog.Reset(file, k, f));
        foreach (var theme in _themes)
        {
            var t = theme.ToLowerInvariant();
            Style(theme);
            var s = State();

            // Apps grouped by category: the Expand all / Collapse all icons over the table
            var apps = Open(new AppsView(Context(s)).Build());
            if (apps.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.Content as string == "Category") is { } byCategory)
            {
                byCategory.IsChecked = true;
                Settle();
            }

            Shot(apps, $"feature-apps-grouped-{t}");
            apps.Close();

            // Models: the compare tab, then the editor with its date picker and the clear button
            var models = new Window
            {
                Width = 1280,
                Height = 900
            };
            var mx = Context(new AppState { State = new UiState() }, models);
            var view = new ModelsView(mx, edits, _ => { });
            models.Content = new Border { Child = view.Build(null) }.Themed(Border.BackgroundProperty, W.MainKey);
            models.Show();
            Shot(models, $"feature-models-{t}");
            models.Close();

            var editor = Dialogs.ModelEditor(edits.Rows().Single(r => r.Key == "claude-opus-5-5"), null, edits);
            editor.Show();
            Settle();
            Shot(editor, $"feature-model-editor-{t}");
            editor.Close();

            var add = Dialogs.ModelEditor(null, "my-local", edits);
            add.Show();
            Settle();
            Shot(add, $"feature-model-add-{t}");
            add.Close();

            // Export and import
            var export = Dialogs.ExportChoice(_ => { });
            export.Show();
            Settle();
            Shot(export, $"feature-export-{t}");
            export.Close();

            var plan = new ImportPlan([], false,
                [new(DataParts.Settings, "theme", "System", "Dark"), new(DataParts.Models, "claude-opus-5-5 · price.input", "4", "5")],
                ["/gone/demo"], ["Settings: checkUpdates is not imported (network calls stay as they are here)."]);
            var import = Dialogs.ImportPreview(plan, _ => { });
            import.Show();
            Settle();
            Shot(import, $"feature-import-{t}");
            import.Close();
        }
    }
}
