// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Models page: the model data (context, max output, prices, retirement) with your changes from models.json, which costs use.
/// Values you changed are in the accent color; Edit changes one model, Reset drops your changes, Add model adds one the data lacks.
/// Opened as <c>Models:&lt;name&gt;</c> from the Usage page, it searches for that model, or offers to add it when the data has none.
/// </summary>
internal partial class ModelsView(Ctx x, ModelEdits? edits = null, Action<UiState>? saveState = null)
{
    private readonly Action<UiState> _save = saveState ?? (s => StateStore.Save(s));
    private readonly ModelEdits _edits = edits ?? ModelEdits.Mine;
    private string _query = "";
    private string _provider = "";
    private string? _selected;
    private List<ModelRow> _rows = [];
    private ContentControl _table = new();
    private TextBox? _box;
    private readonly List<(CheckBox Box, string Key)> _boxes = [];   // the row checkboxes built so far, to update in place
    private bool _syncing;   // the boxes are being set from _picked: their change events are not ticks
    private readonly List<string> _picked = [];   // models ticked for the comparison, in the order they were picked (that is their color)
    private ContentControl _compare = new();

    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave() { _table = new(); _compare = new(); _box = null; _selected = null; }

    public Control Build(string? select = null)
    {
        _rows = _edits.Rows();
        _picked.Clear();   // what you ticked is remembered in state.json: the same models show again on the next visit and the next start
        _picked.AddRange(x.S.State.ComparedModels.Where(k => _rows.Any(r => r.Key == k)).Distinct().Take(ModelCompare.MaxModels));
        if (select != null && select != _selected)
        {
            _selected = select;
            _query = select;
            _provider = "";
            _box = null;
            if (!_rows.Any(r => r.Key == select))
            {
                Dispatcher.UIThread.Post(() => Edit(null, select));
            }
        }

        RefreshBenchmarksWhenDue();
        var head = Head();
        var add = Btn("Add model", () => Edit(null, null), true, "add");
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        head.Children.Add(H1("Models"));
        var table = ModelCatalog.Load();
        var intro = Txt($"{ReadableLog.Plural(_rows.Count, "model")} from model data of {table.Updated} ({(table.Downloaded ? "downloaded" : "built in")}), with your changes. " +
            "Prices are USD per 1M tokens; costs on the Usage page use them. Values you changed are in the accent color; they are saved in models.json in the data folder. " +
            $"Tick {ModelCompare.MinModels} to {ModelCompare.MaxModels} models to compare them.",
            TextSize.Small, false, Muted);
        var providers = _rows.GroupBy(r => r.Provider).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => ($"{g.Key} {g.Count():N0}", g.Key));
        var pills = Pills([($"All {_rows.Count:N0}", ""), .. providers], _provider, v => { _provider = v; x.Refresh(); });
        _box ??= SearchBox("Search model or provider").With(b => b.Text = _query).OnPause(t => { _query = t.Trim(); Fill(); });
        Fill();
        return Page(head, intro, pills, _box.Free(), _compare.Free(), _table.Free());
    }

    private void Fill()
    {
        _boxes.Clear();
        var words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = _rows.Where(r => _provider == "" || r.Provider == _provider)
            .Where(r => words.All(w => $"{r.Key} {r.Provider}".Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        var rows = shown.Select(r => (Func<TableRow>)(() => new TableRow(
        [
            Pick(r),
            r.Added ? Row(Txt(r.Key, TextSize.Body, true), Chip("Yours")) : Txt(r.Key, TextSize.Body, true),
            Txt(r.Provider, TextSize.Small, false, Muted),
            .. ModelCatalog.Fields.Where(f => f != "price.cacheWrite1h").Select(f => Value(r, f)),
            Row(IconBtn("edit", "Edit", () => Edit(r, null)), r.Added || r.Changed.Count > 0
                ? IconBtn(r.Added ? "delete" : "arrow_reset", r.Added ? "Remove this model" : "Reset to the model data", () => Reset(r))
                : new Control())
        ], () => Edit(r, null), r.Key == _selected))).ToList();   // a row's controls are made when it comes near the screen (Table.Virtual)
        FillCompare();
        _table.Content = TableSurface(Table.Virtual([Cols.Check(), new Col("Name", "2*", Min: 120), new Col("Provider", "1*", Min: 64), new Col("Context", "1*", true), new Col("Max output", "1*", true),
            new Col("Input", "1*", true), new Col("Output", "1*", true), new Col("Cache read", "1*", true), new Col("Cache write", "1*", true), new Col("Retires", "1.2*", true), new Col("", "80")],
            rows, "Nothing matches. Clear the search or choose All."));
    }

    /// <summary>The checkbox of a row; a fifth model cannot be ticked until one is unticked.</summary>
    private CheckBox Pick(ModelRow r)
    {
        var on = _picked.Contains(r.Key);
        var cb = new CheckBox { IsChecked = on, IsEnabled = on || _picked.Count < ModelCompare.MaxModels };
        ToolTip.SetTip(cb, on ? "Remove from the comparison" : cb.IsEnabled ? "Add to the comparison" : $"At most {ModelCompare.MaxModels} models");
        _boxes.Add((cb, r.Key));
        cb.IsCheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            if (cb.IsChecked == true && !_picked.Contains(r.Key))
            {
                _picked.Add(r.Key);
            }
            else if (cb.IsChecked != true)
            {
                _picked.Remove(r.Key);
            }

            Remember();
            FillCompare();
            SyncBoxes();   // only the charts and the other boxes change: the table is not built again
        };
        return cb;
    }

    /// <summary>Ticks, enables and explains each row's box from what is picked (a fifth model cannot be ticked).</summary>
    private void SyncBoxes()
    {
        _boxes.RemoveAll(b => b.Box.GetVisualParent() == null);   // boxes of rows that were thrown away when they left the screen
        _syncing = true;
        foreach (var (box, key) in _boxes)
        {
            var on = _picked.Contains(key);
            box.IsChecked = on;
            box.IsEnabled = on || _picked.Count < ModelCompare.MaxModels;
            ToolTip.SetTip(box, on ? "Remove from the comparison" : box.IsEnabled ? "Add to the comparison" : $"At most {ModelCompare.MaxModels} models");
        }

        _syncing = false;
    }

    private void Remember()
    {
        x.S.State.ComparedModels = [.. _picked];
        _save(x.S.State);
    }

    /// <summary>The radar card above the list: shown once two models are ticked.</summary>
    private void FillCompare()
    {
        var models = _picked.Select(k => _rows.FirstOrDefault(r => r.Key == k)).OfType<ModelRow>().ToList();
        if (models.Count < ModelCompare.MinModels)
        {
            _compare.Content = models.Count == 1 ? Txt($"Tick {ModelCompare.MinModels - 1} more to compare (up to {ModelCompare.MaxModels}).", TextSize.Small, false, Muted) : null;
            return;
        }

        var scores = ModelCompare.Score(models, _rows);
        var legend = new StackPanel { Spacing = Space.M, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, MinWidth = Widths.LabelL };
        for (var i = 0; i < models.Count; i++)
        {
            var dot = new Border { Width = Sizes.Dot, Height = Sizes.Dot, CornerRadius = new CornerRadius(Radius.Pill) }.Themed(Border.BackgroundProperty, RadarChart.SeriesKeys[i]);
            legend.Children.Add(Row(dot, Txt(models[i].Key, TextSize.Body, true), Txt(models[i].Provider, TextSize.Small, false, Muted)));
        }

        legend.Children.Add(Txt("Each axis runs from the worst (0%) to the best (100%) of all models: the biggest size or the lowest price is best (100% on a price is only for free). The scale is logarithmic. Hover a corner for the real value.", TextSize.Caption, false, Muted)
            .With(t => t.MaxWidth = Widths.LabelL * 1.5));
        var body = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = Space.XXL, Children = { RadarChart.Build(ModelCompare.Axes, scores, [.. RadarChart.SeriesKeys.Take(models.Count)], RawText), legend } };
        _compare.Content = new StackPanel
        {
            Spacing = Space.XL,
            Children = { Section("data_pie", "Compare models", body, Btn("Clear", () => { _picked.Clear(); Remember(); FillCompare(); SyncBoxes(); })), BenchmarkCard(models) }
        };
    }

    private static string RawText(int axis, double? v)
    {
        if (v == null)
        {
            return "unknown";
        }

        var field = ModelCompare.Axes[axis].Field;
        return Format(field, v.Value.ToString(CultureInfo.InvariantCulture)) + (field.StartsWith("price.", StringComparison.Ordinal) ? " per 1M" : "");
    }

    /// <summary>One value, formatted; yours in the accent color with the data's value in its tooltip.</summary>
    private static TextBlock Value(ModelRow r, string field)
    {
        var v = r.Values.GetValueOrDefault(field);
        var t = Txt(v == null ? "–" : Format(field, v), TextSize.Small, false, r.Changed.Contains(field) ? Accent : v == null ? Muted : W.Text);
        if (r.Changed.Contains(field))
        {
            var data = r.Data.GetValueOrDefault(field);
            ToolTip.SetTip(t, $"Your value. Model data: {(data == null ? "unknown" : Format(field, data))}");
        }
        return t;
    }

    internal static string Format(string field, string v) =>
        field == "retires" ? v
        : field.StartsWith("price.", StringComparison.Ordinal) && decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? "$" + d.ToString("0.00##", CultureInfo.InvariantCulture)
        : long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? UsageCard.Fmt(n)
        : v;

    private async void Edit(ModelRow? row, string? name)
    {
        if (await Dialogs.Model(x.Owner, row, name, _edits))
        {
            _selected = row?.Key ?? name?.Trim().ToLowerInvariant();
            x.Refresh();
        }
    }

    private async void Reset(ModelRow r)
    {
        var what = r.Added ? $"Remove {r.Key} from your models?" : $"Drop your changes to {r.Key} ({string.Join(", ", r.Changed.Select(ModelCatalog.Label))}) and use the model data's values?";
        if (await Dialogs.Confirm(x.Owner, r.Added ? "Remove model" : "Reset model", what, r.Added ? "Remove" : "Reset"))
        {
            if (_edits.Reset(r.Key, null) is { } why)
            {
                Toast.Show(why, "warning", 6);
            }

            x.Refresh();
        }
    }
}
