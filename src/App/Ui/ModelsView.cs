// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Threading;

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
    private readonly HashSet<string> _offProviders = [], _offChanges = [];
    private FilterBar<ModelRow>? _bar;
    private string? _selected;
    private List<ModelRow> _rows = [];
    private ContentControl _table = new();
    private TextBox? _box;
    private readonly List<string> _picked = [];   // the models in the slots, in slot order (that is their color)
    private readonly string?[] _slots = new string?[ModelCompare.MaxModels];   // the Compare tab's four boxes
    private ContentControl _results = new();   // under the slots: facts and charts
    private ContentControl _catalogPane = new(), _comparePane = new();
    private TabControl? _tabs;

    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave()
    {
        _table = new();
        _results = new();
        _catalogPane = new();
        _comparePane = new();
        _tabs = null;
        _box = null;
        _selected = null;
        _bar?.Leave();
    }

    /// <summary>The catalog's tool bar: the search, Provider (most models first) and Changes (the model data, changed by you, added by you).</summary>
    private FilterBar<ModelRow> Bar()
    {
        if (_bar != null)
        {
            return _bar;
        }

        _bar = new FilterBar<ModelRow>(() => _query, Fill, () =>
        {
            _query = "";
            _box?.Text = "";
        });
        _bar.Add("Provider", _offProviders, r => [r.Provider], order: vs => vs.OrderByDescending(v => _rows.Count(r => r.Provider == v)).ThenBy(v => v, StringComparer.Ordinal));
        _bar.Add("Changes", _offChanges, r => [r.Added ? "Added by you" : r.Changed.Count > 0 ? "Changed by you" : "Model data"],
            order: vs => vs.OrderBy(v => v switch { "Model data" => 0, "Changed by you" => 1, _ => 2 }));
        return _bar;
    }

    public Control Build(string? select = null)
    {
        _rows = _edits.Rows();
        LoadSlots();   // the models in the slots are remembered in state.json: they show again on the next visit and the next start
        if (select != null && select != _selected)
        {
            _selected = select;
            _query = select;
            _bar?.Clear();
            _box = null;
            x.S.State.ModelsTab = "Catalog";   // a model sent from the Usage page is looked up in the catalog
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
            "Prices are USD per 1M tokens; costs on the Usage page use them. Values you changed are in the accent color; they are saved in models.json in the data folder.",
            TextSize.Small, false, Muted);
        _box ??= SearchBox("Search model or provider").With(b => b.Text = _query).OnPause(t =>
        {
            _query = t.Trim();
            Fill();
        });
        var bar = Bar().Build(_box);
        Fill();
        _catalogPane.Content = new StackPanel
        {
            Spacing = Space.L,
            Children = { bar, _table.Free() }
        };
        var compareOpen = x.S.State.ModelsTab == "Compare";
        if (compareOpen)
        {
            FillComparePane();   // built only when the tab is open; opening it later builds it then
        }

        _tabs = new TabControl
        {
            ItemsSource = new[] { new TabItem
            {
                Header = "Catalog",
                Content = _catalogPane.Free()
            }, new TabItem
            {
                Header = "Compare",
                Content = _comparePane.Free()
            } },
            SelectedIndex = compareOpen ? 1 : 0
        };
        _tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != _tabs)
            {
                return;   // a combo box inside a tab said it
            }

            x.S.State.ModelsTab = _tabs.SelectedIndex == 1 ? "Compare" : "Catalog";
            _save(x.S.State);
            if (_tabs.SelectedIndex == 1)
            {
                FillComparePane();
            }
        };
        return Page(head, intro, _tabs);
    }

    private void Fill()
    {
        var words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = Bar().Apply([.. _rows.Where(r => words.All(w => $"{r.Key} {r.Provider}".Contains(w, StringComparison.OrdinalIgnoreCase)))], _rows);
        var rows = shown.Select(r => (Func<TableRow>)(() => new TableRow(
        [
            r.Added ? Row(Txt(r.Key, TextSize.Body, true), Chip("Yours")) : Txt(r.Key, TextSize.Body, true),
            Txt(r.Provider, TextSize.Small, false, Muted),
            .. ModelCatalog.Fields.Where(f => f != "price.cacheWrite1h").Select(f => Value(r, f)),
            Row(IconBtn("data_pie", "Compare with other models", () => Compare(r)), IconBtn("edit", "Edit", () => Edit(r, null)), r.Added || r.Changed.Count > 0
                ? IconBtn(r.Added ? "delete" : "arrow_reset", r.Added ? "Remove this model" : "Reset to the model data", () => Reset(r))
                : new Control())
        ], () => Edit(r, null), r.Key == _selected))).ToList();   // a row's controls are made when it comes near the screen (Table.Virtual)
        _table.Content = TableSurface(Table.Virtual([new Col("Name", "2*", Min: 120), new Col("Provider", "1*", Min: 64), new Col("Context", "1*", true), new Col("Max output", "1*", true),
            new Col("Input", "1*", true), new Col("Output", "1*", true), new Col("Cache read", "1*", true), new Col("Cache write", "1*", true), new Col("Retires", "1.2*", true), Cols.Actions()],
            rows, "Nothing matches. Clear the search or filters."));
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

    private void Edit(ModelRow? row, string? name) => Fire.Forget(EditAsync(row, name));

    private async Task EditAsync(ModelRow? row, string? name)
    {
        if (await Dialogs.Model(x.Owner, row, name, _edits))
        {
            _selected = row?.Key ?? name?.Trim().ToLowerInvariant();
            x.Refresh();
        }
    }

    private void Reset(ModelRow r) => Fire.Forget(ResetAsync(r));

    private async Task ResetAsync(ModelRow r)
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
