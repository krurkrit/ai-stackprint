// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Reads and saves your model changes; tests use a temp file instead of ~/.ai-stackprint/models.json.</summary>
internal sealed record ModelEdits(Func<List<ModelRow>> Rows, Func<string, IReadOnlyDictionary<string, string?>, string?> Save, Func<string, string?, string?> Reset)
{
    public static ModelEdits Mine { get; } = new(ModelCatalog.Rows, ModelCatalog.Save, ModelCatalog.Reset);
}

internal static partial class Dialogs
{
    /// <summary>Edits one model (<paramref name="row"/>), or adds one (null; <paramref name="name"/> fills in its name). True when saved.</summary>
    public static Task<bool> Model(Window owner, ModelRow? row, string? name, ModelEdits edits) => ModelEditor(row, name, edits).ShowDialog<bool>(owner);

    /// <summary>The dialog of <see cref="Model"/>: each value in use in a box; a value equal to the model data's is not saved as yours.</summary>
    internal static Window ModelEditor(ModelRow? row, string? name, ModelEdits edits)
    {
        Window? w = null;
        var key = new TextBox { Text = name ?? "", PlaceholderText = "Model name as the app logs it, e.g. my-model", Width = Widths.Field, HorizontalAlignment = HorizontalAlignment.Left };
        var boxes = new Dictionary<string, TextBox>();
        var grid = new Grid { ColumnDefinitions = new($"{Widths.Label},*,Auto"), ColumnSpacing = Space.L, RowSpacing = Space.S };
        var r = 0;
        void Add(Control label, Control box, Control? after = null)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(label, r);
            Grid.SetRow(box, r);
            grid.Children.Add(label);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            if (after != null)
            {
                Grid.SetRow(after, r);
                Grid.SetColumn(after, 2);
                grid.Children.Add(after);
            }
            r++;
        }

        if (row == null)
        {
            Add(Label("Name"), key);
        }

        foreach (var f in ModelCatalog.Fields)
        {
            var data = row?.Data.GetValueOrDefault(f);
            var box = new TextBox { Text = row?.Values.GetValueOrDefault(f) ?? "", PlaceholderText = Hint(f, data), Width = Widths.Field, HorizontalAlignment = HorizontalAlignment.Left };
            boxes[f] = box;
            var reset = row?.Changed.Contains(f) == true ? IconBtn("arrow_reset", $"Reset to the model data ({data ?? "unknown"})", () => box.Text = data ?? "") : null;
            Add(Label(ModelCatalog.Label(f)), box, reset);
        }

        var error = Txt("", TextSize.Small, false, ErrorKey).With(t => t.IsVisible = false);
        var intro = row == null
            ? "A model the model data does not have, such as a cloud or local one. Prices are USD per 1M tokens; an empty value is unknown."
            : $"{row.Key} ({row.Provider}). Prices are USD per 1M tokens. A value you change is saved in models.json and used instead of the model data's; set it back, or empty it, to use the data again.";
        var save = Btn("Save", () =>
        {
            var why = edits.Save(row?.Key ?? key.Text ?? "", boxes.ToDictionary(b => b.Key, b => b.Value.Text));
            if (why == null)
            {
                w!.Close(true);
            }
            else
            {
                error.Text = why;
                error.IsVisible = true;
            }
        }, true).With(b => b.IsDefault = true);
        var body = new StackPanel { Spacing = Space.L, Children = { Txt(intro, TextSize.Small, false, Muted), grid, error } };
        w = Shell(row == null ? "Add model" : "Edit model", body, Btn("Cancel", () => w!.Close(false)).With(b => b.IsCancel = true), save);
        return w;
    }

    private static TextBlock Label(string s) => Txt(s, TextSize.Small).With(t => t.VerticalAlignment = VerticalAlignment.Center);

    private static string Hint(string field, string? data) =>
        data != null ? data + " in the model data"
        : field == "retires" ? "YYYY-MM-DD"
        : field.StartsWith("price.", StringComparison.Ordinal) ? "USD per 1M tokens"
        : "tokens";
}
