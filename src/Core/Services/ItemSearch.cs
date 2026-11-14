// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Searches a list of skills or plugins (<see cref="SearchQuery"/>). Compares the items' own text ignoring case (no lower-case copies are kept:
/// the index holds only each item's author and app name). Every word must match somewhere; a field:value only in that field. Results are ranked:
/// a word in the name counts most (whole name, then its start, then the start of a word in it), then keywords, author, description and path.
/// Ties, and a search with no words, keep the order the items were given in.
/// </summary>
public sealed class ItemSearch
{
    private readonly List<Entry> _entries;

    /// <param name="items">The items to search.</param>
    /// <param name="author">Who made each item (<see cref="ItemAuthor.For"/>); the item's own author when not given.</param>
    public ItemSearch(IEnumerable<Item> items, Func<Item, string>? author = null)
    {
        author ??= i => i.Author ?? "";
        _entries = items.Select(i => new Entry(i, author(i))).ToList();
    }

    public List<Item> Find(string? query) => Find(SearchQuery.Parse(query));

    public List<Item> Find(SearchQuery query)
    {
        if (query.IsEmpty)
        {
            return _entries.Select(e => e.Item).ToList();
        }

        return _entries.Select(e => (e.Item, Score: Score(e, query)))
            .Where(r => r.Score >= 0)
            .OrderByDescending(r => r.Score)   // stable: equal scores keep the given order
            .Select(r => r.Item)
            .ToList();
    }

    /// <summary>The item's rank for the query, higher first; -1 when a term does not match.</summary>
    private static int Score(Entry e, SearchQuery q)
    {
        var total = 0;
        foreach (var t in q.Terms)
        {
            var s = t.Field == null ? Any(e, t.Text) : Field(e, t) ? 0 : -1;
            if (s < 0)
            {
                return -1;
            }

            total += s;
        }

        return total;
    }

    /// <summary>The best place a free word or phrase matches, or -1.</summary>
    private static int Any(Entry e, string w)
    {
        if (e.Name.Equals(w, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (e.Name.StartsWith(w, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (WordStart(e.Name, w))
        {
            return 60;
        }

        if (e.Name.Contains(w, StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        if (e.Keywords.Contains(w, StringComparer.OrdinalIgnoreCase))
        {
            return 35;
        }

        if (e.Keywords.Any(k => k.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return 25;
        }

        if (e.Author.Contains(w, StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if (WordStart(e.Desc, w))
        {
            return 15;
        }

        if (e.Desc.Contains(w, StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        return e.Path.Contains(w, StringComparison.OrdinalIgnoreCase) ? 5 : -1;
    }

    /// <summary>A field:value term: the value occurs in that field, or with quotes is the whole field.</summary>
    private static bool Field(Entry e, SearchTerm t)
    {
        bool Has(string v) => t.Exact ? v.Equals(t.Text, StringComparison.OrdinalIgnoreCase) : v.Contains(t.Text, StringComparison.OrdinalIgnoreCase);
        return t.Field switch
        {
            "name" => Has(e.Name),
            "desc" => Has(e.Desc),
            "author" => Has(e.Author),
            "app" => Has(e.AppId) || Has(e.AppName),
            "level" => Has(e.Level) || (e.LevelLabel != "" && Has(e.LevelLabel)),
            "keyword" => e.Keywords.Any(Has),
            "path" => Has(e.Path),
            _ => false
        };
    }

    /// <summary>The word starts <paramref name="text"/> or follows a character that is not a letter or digit (my-skill, code_review, "the tool").</summary>
    private static bool WordStart(string text, string w)
    {
        for (var i = text.IndexOf(w, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(w, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (i == 0 || !char.IsLetterOrDigit(text[i - 1]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An item with the two texts that are worked out, not stored on it.</summary>
    private sealed class Entry(Item item, string author)
    {
        public Item Item { get; } = item;
        public string Name => Item.Name;
        public string Desc => Item.Description;
        public string Path => Item.Path;
        public string Author { get; } = author;
        public string AppId => Item.App;
        public string AppName { get; } = item.Source.IndexOf(" · ", StringComparison.Ordinal) is var i and >= 0 ? item.Source[..i] : item.Source;   // a plugin's skills: "Claude Code · plugin"
        public string Level => Item.Level;
        public string LevelLabel => Item.LevelLabel ?? "";
        public IReadOnlyList<string> Keywords => Item.Keywords;
    }
}
