// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The tool bar of a page with filters: the search, a filter menu per field (shown when it offers two values or more), Group by toggles and Reset at the end
/// (dimmed while all is as at first), in one row when the window is wide enough; a line under it lists what each filter lets through, the grouping and how many are shown.
/// A page registers its fields once (<see cref="Add"/>), builds the bar once per visit (a menu stays open while its check boxes change the list),
/// and on each change passes the items left after the search to <see cref="Apply"/>, which offers the values with counts and returns the items shown.
/// </summary>
internal sealed partial class FilterBar<T>
{
    private sealed class Field(string label, HashSet<string> off, Func<T, IEnumerable<string>> values, Func<string, string> name, Func<IEnumerable<string>, IEnumerable<string>> order)
    {
        public string Label { get; } = label;
        public HashSet<string> Off { get; } = off;
        public Func<T, IEnumerable<string>> Values { get; } = values;
        public Func<string, string> Name { get; } = name;
        public Func<IEnumerable<string>, IEnumerable<string>> Order { get; } = order;
        public List<(string Label, string Value, int Count)> Opts { get; set; } = [];
        public FilterMenu? Menu { get; set; }
        public string? Only { get; set; }   // show only this value at the next Apply (a link from another page)
        public bool Lets(T item) => Values(item).Any(v => !Off.Contains(v));
    }

    private readonly List<Field> _fields = [];
    private readonly Action _changed, _clearSearch;
    private readonly Func<string> _search;
    private (List<(string Label, string Value)> Opts, HashSet<string> On, string[] Start)? _group;
    private List<(string Label, string Value)> _groupOffered = [];
    private ContentControl? _groupHost;
    private Button? _reset;
    private TextBlock? _summary;
    private int _shown, _total;

    /// <param name="search">The search text now.</param>
    /// <param name="changed">Re-runs the page's list (a filter, the grouping or Reset changed).</param>
    /// <param name="clearSearch">Empties the page's search box and query (Reset).</param>
    public FilterBar(Func<string> search, Action changed, Action clearSearch)
    {
        _search = search;
        _changed = changed;
        _clearSearch = clearSearch;
    }

    /// <summary>A field: its menu label, the unchecked values (kept by the page), each item's values (an item with several shows while any is on), how a value is named and ordered.</summary>
    public void Add(string label, HashSet<string> off, Func<T, IEnumerable<string>> values, Func<string, string>? name = null, Func<IEnumerable<string>, IEnumerable<string>>? order = null) =>
        _fields.Add(new Field(label, off, values, name ?? (v => v), order ?? (vs => vs.Order(StringComparer.OrdinalIgnoreCase))));

    /// <summary>Group by toggles: each on or off on its own; <paramref name="start"/> is the grouping a visit starts with (Reset puts it back).</summary>
    public void GroupBy(IEnumerable<(string Label, string Value)> opts, HashSet<string> on, params string[] start) => _group = ([.. opts], on, start);

    /// <summary>Show only <paramref name="value"/> of the field <paramref name="label"/> (the others unchecked) at the next <see cref="Apply"/>.</summary>
    public void Only(string label, string value)
    {
        if (_fields.FirstOrDefault(f => f.Label == label) is { } f)
        {
            f.Only = value;
        }
    }

    /// <summary>Puts every filter and the grouping back as at first (the search is the page's: it clears its own).</summary>
    public void Clear()
    {
        foreach (var f in _fields)
        {
            f.Off.Clear();
            f.Only = null;
        }

        if (_group is { } g)
        {
            g.On.Clear();
            g.On.UnionWith(g.Start);
        }
    }

    /// <summary>
    /// Offers each field's values among <paramref name="searched"/> (the items the search lets through), each with how many items have it among those the other
    /// filters let through, and returns the items every filter lets through. Unchecked values none of <paramref name="all"/> has any more are dropped (nothing
    /// would undo them); a search does not drop them, so they apply again when it is cleared. <paramref name="groupOffered"/>: the Group by toggles shown now
    /// (null: all of them); a grouping not shown stays on for later.
    /// </summary>
    public List<T> Apply(IReadOnlyCollection<T> searched, IReadOnlyCollection<T> all, IEnumerable<string>? groupOffered = null)
    {
        foreach (var f in _fields)
        {
            var values = f.Order(searched.SelectMany(f.Values).Distinct()).ToList();
            if (f.Only is { } only && values.Contains(only))
            {
                f.Off.Clear();
                f.Off.UnionWith(values.Where(v => v != only));
            }

            f.Only = null;
            KeepOffered(f.Off, [.. all.SelectMany(f.Values).Distinct()]);
            var others = searched.Where(i => _fields.All(o => o == f || o.Lets(i))).ToList();
            f.Opts = [.. values.Select(v => (f.Name(v), v, others.Count(i => f.Values(i).Contains(v))))];
        }

        var shown = searched.Where(i => _fields.All(f => f.Lets(i))).ToList();
        if (_group is { } g)
        {
            var offered = groupOffered?.ToHashSet();
            _groupOffered = [.. g.Opts.Where(o => offered == null || offered.Contains(o.Value))];
        }

        _shown = shown.Count;
        _total = all.Count;
        Refresh();
        return shown;
    }

    /// <summary>The Group by toggles that are on and shown now (a grouping by a value all items share is left out).</summary>
    public IReadOnlySet<string> Grouping => _group is { } g ? g.On.Where(v => _groupOffered.Any(o => o.Value == v)).ToHashSet() : new HashSet<string>();

    /// <summary>The page was left: drops this visit's controls, so they do not keep the old page in memory (the filters and grouping stay).</summary>
    public void Leave()
    {
        foreach (var f in _fields)
        {
            f.Menu?.Release();
            f.Menu = null;
        }

        _groupHost = null;
        _reset = null;
        _summary = null;
    }

    /// <summary>Whether all is as at first: no search, every value of every filter on, the first grouping.</summary>
    public bool AtStart => _search() == "" && _fields.All(f => f.Off.Count == 0) && (_group is not { } g || g.On.SetEquals(g.Start));

    private void Refresh()
    {
        foreach (var f in _fields)
        {
            if (f.Menu is { } m)
            {
                m.IsVisible = f.Opts.Count > 1;
                m.Update(f.Opts.Count);
            }
        }

        if (_groupHost != null && _group is { } g)
        {
            _groupHost.IsVisible = _groupOffered.Count > 0;
            _groupHost.Content = _groupOffered.Count == 0 ? null : W.Row(Txt("Group by", TextSize.Small, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center),
                Toggles(_groupOffered, g.On, v =>
                {
                    if (!g.On.Remove(v))
                    {
                        g.On.Add(v);
                    }

                    _changed();
                }));
        }

        Summarize();
        _reset?.IsEnabled = !AtStart;
    }

    /// <summary>"Status: Installed, Not found    Category: Agents    Group by: Category    Shown: 12 of 40": the values on (a filter with one value is left out).</summary>
    private void Summarize()
    {
        if (_summary == null)
        {
            return;
        }

        var parts = _fields.Where(f => f.Opts.Count > 1).Select(f => (f.Label, string.Join(", ", f.Opts.Where(o => !f.Off.Contains(o.Value)).Select(o => o.Label)))).ToList();
        if (_group is { } g && _groupOffered.Count > 0)
        {
            var by = _groupOffered.Where(o => g.On.Contains(o.Value)).Select(o => o.Label).ToList();
            parts.Add(("Group by", by.Count == 0 ? "None" : string.Join(", ", by)));
        }

        parts.Add(("Shown", _shown == _total ? $"{_total:N0}" : $"{_shown:N0} of {_total:N0}"));
        var inlines = new InlineCollection();
        foreach (var (label, values) in parts)
        {
            if (inlines.Count > 0)
            {
                inlines.Add(new Run("    "));
            }

            inlines.Add(new Run(label + ": ") { FontWeight = Emphasis }.With(r => r.Bind(TextElement.ForegroundProperty, _summary.GetResourceObservable(W.Text))));
            inlines.Add(new Run(values));
        }

        _summary.Inlines = inlines;
    }

    /// <summary>The bar for this visit: the search box (taken from its old page) with an optional help button after it, the menus, Group by, Reset, and the summary line under them.</summary>
    public Control Build(TextBox search, Control? help = null)
    {
        var right = new WrapPanel { ItemSpacing = Space.M, LineSpacing = Space.M, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var f in _fields)
        {
            f.Menu = new FilterMenu(f.Label, () => f.Opts, f.Off, _changed);
            right.Children.Add(f.Menu);
        }

        if (_group != null)
        {
            _groupHost = new ContentControl();
            right.Children.Add(_groupHost);
        }

        _reset = IconBtn("arrow_reset", "Reset the search, filters and grouping", () =>
        {
            Clear();
            _clearSearch();
            _changed();
        });
        _reset.Resources["ButtonBackgroundDisabled"] = Avalonia.Media.Brushes.Transparent;   // dimmed: a faded icon, no gray fill
        _reset.Resources["ButtonBorderBrushDisabled"] = Avalonia.Media.Brushes.Transparent;
        var reset = _reset;
        reset.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsEnabledProperty && reset.Content is Control c)
            {
                c.Opacity = reset.IsEnabled ? 1 : .35;
            }
        };
        right.Children.Add(reset);
        search.MinWidth = Widths.Search;
        Control first = search.Free();
        if (help != null)
        {
            DockPanel.SetDock(help, Dock.Right);
            help.VerticalAlignment = VerticalAlignment.Center;
            first = new DockPanel { MinWidth = Widths.Search, Children = { help, first } };
        }

        _summary = Txt("", TextSize.Small, false, Muted).With(t => t.TextWrapping = Avalonia.Media.TextWrapping.Wrap);
        Refresh();
        return new StackPanel
        {
            Spacing = Space.M,
            Children = { new ToolbarPanel { Children = { first, right } }, _summary }
        };
    }
}
