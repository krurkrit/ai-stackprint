// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Ui;

/// <summary>The Apps page's tool bar (<see cref="FilterBar{T}"/>): the search, the Status and Category menus, Group by (Category and Status toggles) and Reset.</summary>
internal partial class AppsView
{
    private const string ByCategory = "category", ByStatus = "status";
    private readonly HashSet<string> _group = [ByCategory];   // Group by: each one on or off; both on groups by both, none on is a plain list
    private FilterBar<AppInfo>? _bar;

    private bool ByCat => _group.Contains(ByCategory);
    private bool ByStat => _group.Contains(ByStatus);

    private FilterBar<AppInfo> Bar()
    {
        if (_bar != null)
        {
            return _bar;
        }

        _bar = new FilterBar<AppInfo>(() => _q, Fill, () =>
        {
            _q = "";
            _search.Text = "";
        });
        _bar.Add("Status", _offStatus, a => [StatusName(a)], order: vs => vs.OrderBy(v => v == "Installed" ? 0 : 1));
        _bar.Add("Category", _offCats, a => [a.Category], Plural);
        _bar.GroupBy([("Category", ByCategory), ("Status", ByStatus)], _group, ByCategory);
        return _bar;
    }
}
