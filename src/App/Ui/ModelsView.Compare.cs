// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The Compare tab of the Models page: four type-ahead boxes pick 2 to 4 models; under them a facts table, the radar and the benchmarks.</summary>
internal partial class ModelsView
{
    private bool _syncingSlots;   // a box is being set from its slot: its events are not the reader's

    /// <summary>The slots from state.json: the saved models that still exist, first slot first.</summary>
    private void LoadSlots()
    {
        Array.Clear(_slots);
        var keep = x.S.State.ComparedModels.Where(k => _rows.Any(r => r.Key == k)).Distinct().Take(ModelCompare.MaxModels).ToList();
        for (var i = 0; i < keep.Count; i++)
        {
            _slots[i] = keep[i];
        }

        SyncPicked();
    }

    private void SyncPicked()
    {
        _picked.Clear();
        _picked.AddRange(_slots.OfType<string>());
    }

    private void Remember()
    {
        x.S.State.ComparedModels = [.. _picked];
        _save(x.S.State);
    }

    private void SetSlot(int i, string? key)
    {
        _slots[i] = key;
        SyncPicked();
        Remember();
        FillResults();
    }

    /// <summary>The Compare action of a catalog row: the model goes to the first free slot and the tab opens.</summary>
    private void Compare(ModelRow r)
    {
        if (!_slots.Contains(r.Key))
        {
            var free = Array.IndexOf(_slots, null);
            if (free < 0)
            {
                Toast.Show($"All {ModelCompare.MaxModels} slots on the Compare tab are in use. Clear one first.", "warning", 6);
                return;
            }

            _slots[free] = r.Key;
            SyncPicked();
            x.S.State.ComparedModels = [.. _picked];
        }

        x.S.State.ModelsTab = "Compare";
        _save(x.S.State);
        x.Refresh();
    }

    private void FillComparePane()
    {
        var slots = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = Space.L
        };
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = Slot(i);
            Grid.SetColumn(slot, i);
            slots.Children.Add(slot);
        }

        FillResults();
        _comparePane.Content = new StackPanel
        {
            Spacing = Space.XL,
            Children = { slots, _results.Free() }
        };
    }

    /// <summary>One type-ahead box: typing narrows the list to models whose name or provider holds every word; a model in another slot is not offered.</summary>
    private Control Slot(int i)
    {
        var keys = _rows.Select(r => r.Key).ToList();
        var box = new AutoCompleteBox
        {
            ItemsSource = keys,
            Text = _slots[i] ?? "",
            PlaceholderText = $"Model {i + 1}",
            MinimumPrefixLength = 0,
            FilterMode = AutoCompleteFilterMode.Custom,
            ItemFilter = (text, item) => item is string k && (_slots[i] == k || !_slots.Contains(k)) && Matches(k, text),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        box.GotFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(box.Text))
            {
                box.IsDropDownOpen = true;   // an empty box offers the whole list
            }
        };
        box.SelectionChanged += (_, _) =>
        {
            if (!_syncingSlots && box.SelectedItem is string k)
            {
                SetSlot(i, k);
            }
        };
        box.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            // Clicking an item of the list moves the focus into the pop-up first: wait until the box is really left (the click has chosen its item by then)
            if (_syncingSlots || box.IsDropDownOpen || box.IsKeyboardFocusWithin)
            {
                return;
            }

            var typed = (box.Text ?? "").Trim();
            var hit = typed.Length == 0 ? null : keys.FirstOrDefault(k => string.Equals(k, typed, StringComparison.OrdinalIgnoreCase));
            if (typed.Length == 0 && _slots[i] != null)
            {
                SetSlot(i, null);
            }
            else if (hit != null && hit != _slots[i] && !_slots.Contains(hit))
            {
                SetSlot(i, hit);
            }

            _syncingSlots = true;
            box.Text = _slots[i] ?? "";   // text that is no model goes back to what the slot holds
            _syncingSlots = false;
        }, DispatcherPriority.Background);
        var clear = IconBtn("dismiss", "Clear this slot", () =>
        {
            _syncingSlots = true;
            box.Text = "";
            _syncingSlots = false;
            if (_slots[i] != null)
            {
                SetSlot(i, null);
            }
        });
        DockPanel.SetDock(clear, Dock.Right);
        return new DockPanel { Children = { clear, box } };
    }

    private bool Matches(string key, string? text)
    {
        var provider = _rows.FirstOrDefault(r => r.Key == key)?.Provider ?? "";
        return (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => $"{key} {provider}".Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Under the slots: a hint until two models are chosen, then the facts, the radar and the benchmarks.</summary>
    private void FillResults()
    {
        var models = _picked.Select(k => _rows.FirstOrDefault(r => r.Key == k)).OfType<ModelRow>().ToList();
        if (models.Count < ModelCompare.MinModels)
        {
            _results.Content = Txt($"Pick {ModelCompare.MinModels} to {ModelCompare.MaxModels} models above to compare them. You can also use the compare button on a row of the Catalog.", TextSize.Small, false, Muted);
            return;
        }

        var scores = ModelCompare.Score(models, _rows);
        var legend = new StackPanel
        {
            Spacing = Space.M,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = Widths.LabelL
        };
        for (var i = 0; i < models.Count; i++)
        {
            var dot = new Border
            {
                Width = Sizes.Dot,
                Height = Sizes.Dot,
                CornerRadius = new CornerRadius(Radius.Pill)
            }.Themed(Border.BackgroundProperty, RadarChart.SeriesKeys[i]);
            legend.Children.Add(Row(dot, Txt(models[i].Key, TextSize.Body, true), Txt(models[i].Provider, TextSize.Small, false, Muted)));
        }

        legend.Children.Add(Txt("Each axis runs from the worst (0%) to the best (100%) of all models: the biggest size or the lowest price is best (100% on a price is only for free). The scale is logarithmic. Hover a corner for the real value.", TextSize.Caption, false, Muted)
            .With(t => t.MaxWidth = Widths.LabelL * 1.5));
        var body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Space.XXL,
            Children = { RadarChart.Build(ModelCompare.Axes, scores, [.. RadarChart.SeriesKeys.Take(models.Count)], RawText), legend }
        };
        _results.Content = new StackPanel
        {
            Spacing = Space.XL,
            Children =
            {
                Section("document_text", "Facts", Facts(models)),
                Section("data_pie", "Compare models", body, Btn("Clear", ClearSlots)),
                BenchmarkCard(models)
            }
        };
    }

    private void ClearSlots()
    {
        Array.Clear(_slots);
        SyncPicked();
        Remember();
        FillComparePane();
    }

    /// <summary>The values side by side; the best one in each row (the biggest size, the lowest price) is in the accent color.</summary>
    private static Control Facts(List<ModelRow> models)
    {
        var cols = new List<Col> { new("", "1.2*", Min: 100) };
        cols.AddRange(models.Select(m => new Col(m.Key, "1*", true, Min: 100)));
        var rows = ModelCatalog.Fields.Where(f => f != "price.cacheWrite1h").Select(f =>
        {
            var values = models.Select(m => m.Values.GetValueOrDefault(f)).ToList();
            var best = Best(f, values);   // every model with the best value is marked
            return new TableRow([Txt(ModelCatalog.Label(f), TextSize.Small, false, Muted),
                .. values.Select(v => Txt(v == null ? "–" : Format(f, v), TextSize.Small, v == best, v == best ? Accent : v == null ? Muted : W.Text))]);
        });
        return Table.Build([.. cols], rows);
    }

    /// <summary>The best value of a field (the biggest size, the lowest price), or null when they are all the same, unknown or not comparable (a date).</summary>
    internal static string? Best(string field, List<string?> values)
    {
        var nums = values.Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (double?)d : null).ToList();
        var known = nums.OfType<double>().Distinct().ToList();
        if (known.Count < 2 || field == "retires")
        {
            return null;
        }

        return values[nums.IndexOf(field.StartsWith("price.", StringComparison.Ordinal) ? known.Min() : known.Max())];
    }
}
