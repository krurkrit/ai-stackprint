// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Level check boxes for skills and plugins; for skills also Invoked by (one choice: All, Agent only, User only, Agent and User), source and grouping.
/// Shared by the Skills and Plugins pages and an app's Skills section. Only values present are offered; a filter with one value is left out.
/// </summary>
internal class SkillFilter
{
    private static readonly string[] _invokedValues = ["Agent only", "User only", "Agent and User"];
    private readonly HashSet<string> _offLevels = [];   // unchecked levels; empty = all checked
    private string _invoked = "", _from = "";   // "" = all
    private bool _byInvoked, _byFrom;                  // Group by check boxes
    private readonly HashSet<string> _collapsed = [];
    private readonly ComboBox _fromBox = new() { MinWidth = Widths.FieldM };
    private bool _building;
    private Action _changed = () => { };

    public SkillFilter() =>
        _fromBox.SelectionChanged += (_, _) => { if (!_building) { _from = (_fromBox.SelectedItem as Opt)?.Id ?? ""; _changed(); } };

    public void Reset() { _offLevels.Clear(); _invoked = ""; _from = ""; _byInvoked = _byFrom = false; _collapsed.Clear(); }

    public bool Match(Item i) => SkillMatch.Shows(i, _offLevels, _invoked, _from);

    /// <param name="skill">Also Invoked by, From and Group by (plugins have only levels).</param>
    /// <param name="changed">Called after a filter changes: re-run <see cref="Match"/> and rebuild these controls.</param>
    /// <remarks>Call before <see cref="Match"/>: it drops filter values the items no longer have.</remarks>
    public Control Controls(IReadOnlyCollection<Item> items, bool skill, Action changed)
    {
        _changed = changed;
        var levels = Levels.Order.Where(l => items.Any(s => s.Level == l)).ToList();
        var invoked = skill ? _invokedValues.Where(v => items.Any(s => SkillMatch.Label(s) == v)).ToList() : [];
        var froms = skill ? items.Select(AppDetailView.From).Distinct().OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList() : [];
        // Values no longer offered would hide items with no control left to undo it
        KeepOffered(_offLevels, levels);
        if (invoked.Count < 2 || !invoked.Contains(_invoked))
        {
            _invoked = "";
        }

        if (froms.Count < 2 || !froms.Contains(_from))
        {
            _from = "";
        }

        var sp = new StackPanel { Spacing = Space.L };
        if (levels.Count > 1)
        {
            sp.Children.Add(FilterRow("Level", Checks(levels.Select(l => ((Control)Txt(l, TextSize.Body), l)).ToList(), _offLevels, changed), Levels.InfoButton()));
        }

        if (invoked.Count > 1)
        {
            // One choice: the combinations are exclusive. Counts follow the level and source filters.
            var inScope = items.Where(i => SkillMatch.Shows(i, _offLevels, "", _from)).ToList();
            var opts = invoked.Select(v => ($"{v} {inScope.Count(i => SkillMatch.Label(i) == v):N0}", v)).Prepend(($"All {inScope.Count:N0}", ""));
            sp.Children.Add(FilterRow("Invoked by", Radios(opts, _invoked, v => { _invoked = v; changed(); })));
        }

        if (invoked.Count < 2)
        {
            _byInvoked = false;
        }

        if (froms.Count < 2)
        {
            _byFrom = false;
        }

        if (froms.Count > 1)
        {
            _building = true;
            var opts = froms.Select(v => new Opt(v, v)).Prepend(new Opt("", "Any source")).ToList();
            _fromBox.ItemsSource = opts;
            _fromBox.SelectedItem = opts.FirstOrDefault(o => o.Id == _from) ?? opts[0];
            _building = false;
            sp.Children.Add(FilterRow("From", _fromBox.Free()));
        }
        var by = new List<Control>();
        if (invoked.Count > 1)
        {
            by.Add(Check("Invoked by", _byInvoked, v => { _byInvoked = v; changed(); }));
        }

        if (froms.Count > 1)
        {
            by.Add(Check("From", _byFrom, v => { _byFrom = v; changed(); }));
        }

        if (by.Count > 0)
        {
            sp.Children.Add(FilterRow("Group by", [.. by]));
        }

        return sp;
    }

    /// <summary>The rows, under collapsible heading rows (name and count) when grouped; headings sorted, items keep their order.</summary>
    public IEnumerable<TableRow> Rows(List<Item> items, Func<Item, TableRow> row, int cols, Action changed)
    {
        if (!_byInvoked && !_byFrom) { foreach (var i in items) { yield return row(i); } yield break; }
        string Key(Item i) => _byInvoked && _byFrom ? $"{InvokeLabel(i)} · {AppDetailView.From(i)}" : _byInvoked ? InvokeLabel(i) : AppDetailView.From(i);
        foreach (var g in items.GroupBy(Key).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var open = !_collapsed.Contains(g.Key); var key = g.Key;
            var cells = new Control[cols];
            cells[0] = Row(Txt(open ? "▾" : "▸", TextSize.Small, false, Muted), Title(g.Key, TextSize.Body), Txt(g.Count().ToString("N0"), TextSize.Small, false, Muted));
            Grid.SetColumnSpan(cells[0], cols);   // the group title runs across the table
            for (var c = 1; c < cols; c++)
            {
                cells[c] = new Panel();
            }

            yield return new TableRow(cells, () => { if (!_collapsed.Remove(key)) { _collapsed.Add(key); } changed(); });
            if (open)
            {
                foreach (var i in g)
                {
                    yield return row(i);
                }
            }
        }
    }

    private static string InvokeLabel(Item i) => SkillMatch.Label(i);

    private sealed record Opt(string Id, string Name) { public override string ToString() => Name; }
}
