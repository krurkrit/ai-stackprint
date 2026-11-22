// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The filters of a list of skills, plugins or commands and agents, as a <see cref="FilterBar{T}"/>: App (when the list has several), Level, Type (commands and agents),
/// and for skills Invoked by and From with Group by toggles for both. Shared by the Skills, Plugins and Commands &amp; Agents pages and an app's Skills section.
/// The filters and grouping stay while the app runs.
/// </summary>
internal sealed class SkillFilter
{
    private const string ByInvoked = "invoked", ByFrom = "from";
    private static readonly string[] _invokedValues = ["Agent only", "User only", "Agent and User"];
    private readonly HashSet<string> _offApps = [], _offLevels = [], _offTypes = [], _offInvoked = [], _offFrom = [];   // unchecked values; empty = all
    private readonly HashSet<string> _group = [], _collapsed = [];
    private readonly bool _skill;

    /// <param name="kind">"Skill", "Plugin" or "Part" (commands and agents).</param>
    /// <param name="appName">An app's name from its id, for an App filter; null: no App filter (the list is one app's).</param>
    /// <param name="search">The search text now.</param>
    /// <param name="changed">Re-runs the list.</param>
    /// <param name="clearSearch">Empties the search box and query (Reset).</param>
    public SkillFilter(string kind, Func<string, string>? appName, Func<string> search, Action changed, Action clearSearch)
    {
        _skill = kind == "Skill";
        Bar = new FilterBar<Item>(search, changed, () =>
        {
            _collapsed.Clear();
            clearSearch();
        });
        if (appName != null)
        {
            Bar.Add("App", _offApps, i => [i.App], appName, vs => vs.OrderBy(appName, StringComparer.OrdinalIgnoreCase));
        }

        Bar.Add("Level", _offLevels, i => [i.Level], order: vs => Levels.Order.Where(vs.Contains));   // highest priority first
        if (kind == "Part")
        {
            Bar.Add("Type", _offTypes, i => [i.Kind], v => v + "s");   // Commands, Agents
        }

        if (_skill)
        {
            Bar.Add("Invoked by", _offInvoked, i => [SkillMatch.Label(i)], order: vs => _invokedValues.Where(vs.Contains));
            Bar.Add("From", _offFrom, i => [SkillMatch.From(i)]);
            Bar.GroupBy([("Invoked by", ByInvoked), ("From", ByFrom)], _group);
        }
    }

    /// <summary>The tool bar: build it once per visit (<see cref="FilterBar{T}.Build"/>).</summary>
    public FilterBar<Item> Bar { get; }

    /// <summary>The page was left: see <see cref="ItemBrowser.Leave"/>.</summary>
    public void Leave() => Bar.Leave();

    /// <summary>Every filter and the grouping as at first, the headings open (another project or app opened).</summary>
    public void Reset()
    {
        Bar.Clear();
        _collapsed.Clear();
    }

    /// <summary>The items the filters let through, of <paramref name="searched"/> (those the search found, best first); Group by offers a toggle for a field with two values or more.</summary>
    public List<Item> Apply(List<Item> searched, IReadOnlyCollection<Item> all)
    {
        var offered = new List<string>();
        if (_skill && searched.Select(SkillMatch.Label).Distinct().Skip(1).Any())
        {
            offered.Add(ByInvoked);
        }

        if (_skill && searched.Select(SkillMatch.From).Distinct().Skip(1).Any())
        {
            offered.Add(ByFrom);
        }

        return Bar.Apply(searched, all, offered);
    }

    /// <summary>The rows, under collapsible heading rows (name and count) when grouped; headings sorted, items keep their order.</summary>
    public IEnumerable<TableRow> Rows(List<Item> items, Func<Item, TableRow> row, int cols, Action changed) => RowMakers(items, row, cols, changed).Select(m => m());

    /// <summary>The same rows as makers: a row's controls are built when its maker is called (a long list builds only the rows on screen, see <see cref="Table.Virtual"/>).</summary>
    public List<Func<TableRow>> RowMakers(List<Item> items, Func<Item, TableRow> row, int cols, Action changed)
    {
        var res = new List<Func<TableRow>>();
        var on = Bar.Grouping;
        bool byInvoked = on.Contains(ByInvoked), byFrom = on.Contains(ByFrom);
        if (!byInvoked && !byFrom)
        {
            res.AddRange(items.Select(i => (Func<TableRow>)(() => row(i))));
            return res;
        }

        string Key(Item i) => byInvoked && byFrom ? $"{SkillMatch.Label(i)} · {SkillMatch.From(i)}" : byInvoked ? SkillMatch.Label(i) : SkillMatch.From(i);
        foreach (var g in items.GroupBy(Key).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var open = !_collapsed.Contains(g.Key);
            var key = g.Key;
            var count = g.Count();
            res.Add(() =>
            {
                var cells = new Control[cols];
                cells[0] = Row(Icon(open ? "chevron_down" : "chevron_right", IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Muted), Title(key, TextSize.Body), Txt(count.ToString("N0"), TextSize.Small, false, Muted));
                Grid.SetColumnSpan(cells[0], cols);   // the group title runs across the table
                for (var c = 1; c < cols; c++)
                {
                    cells[c] = new Panel();
                }

                return new TableRow(cells, () =>
                {
                    if (!_collapsed.Remove(key)) { _collapsed.Add(key); }
                    changed();
                });
            });
            if (open)
            {
                res.AddRange(g.Select(i => (Func<TableRow>)(() => row(i))));
            }
        }

        return res;
    }
}
