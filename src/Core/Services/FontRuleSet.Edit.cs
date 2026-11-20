// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

public sealed partial class FontRuleSet
{
    /// <summary>True when making <paramref name="candidate"/> the parent of <paramref name="key"/> would make a loop (the candidate is the setting itself, or leads back to it).</summary>
    public bool WouldCycle(string key, string candidate)
    {
        var seen = new HashSet<string>();
        for (var c = candidate; c.Length > 0 && seen.Add(c); c = rules.TryGetValue(c, out var r) ? r.Parent : "")
        {
            if (c == key)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Settings that can be the parent of <paramref name="key"/> (none would make a loop).</summary>
    public IEnumerable<string> ParentChoices(string key) => rules.Keys.Where(k => k != key && !WouldCycle(key, k));

    /// <summary>
    /// Sets the parent; empty = none. Without a parent every field becomes its own value (what it is now). With one, adjusted fields keep their look
    /// (the offset is worked out again); linked and own fields stay as they are. Throws on an unknown parent or a loop.
    /// </summary>
    public void SetParent(string key, string parent)
    {
        var r = rules[key];
        if (parent.Length > 0 && !rules.ContainsKey(parent))
        {
            throw new ArgumentException($"Unknown parent \"{parent}\"", nameof(parent));
        }

        if (parent.Length > 0 && WouldCycle(key, parent))
        {
            throw new InvalidOperationException($"Cycle at \"{key}\"");
        }

        var now = Resolve(key);
        r.Family.Mode = r.Of(r.Family);   // a field that has no mode of its own keeps the one it has now, whatever the new parent is
        r.Size.Mode = r.Of(r.Size);
        r.Weight.Mode = r.Of(r.Weight);
        r.Parent = parent;
        if (parent.Length == 0)
        {
            foreach (var p in new[] { FontProp.Family, FontProp.Size, FontProp.Weight })
            {
                ToSet(r, p, now);
            }

            return;
        }

        var up = Resolve(parent);
        if (r.Size.Mode == FontMode.Adjust)
        {
            r.Size.Value = Math.Round(now.Size - up.Size, 4);
        }

        if (r.Weight.Mode == FontMode.Adjust)
        {
            r.Weight.Value = Math.Round((double)(now.Weight - up.Weight), 4);
        }
    }

    /// <summary>Changes how a field gets its value, keeping what it looks like now: own = what it resolves to, adjust = the difference to the parent. Link and adjust need a parent.</summary>
    public void SetMode(string key, FontProp prop, FontMode mode)
    {
        var r = rules[key];
        if (mode != FontMode.Set && (r.Parent.Length == 0 || !rules.ContainsKey(r.Parent)))
        {
            throw new InvalidOperationException($"\"{key}\" has no parent to {(mode == FontMode.Link ? "link" : "adjust")} to");
        }

        if (mode == FontMode.Adjust && prop == FontProp.Family)
        {
            throw new InvalidOperationException("A family cannot be adjusted");
        }

        var now = Resolve(key);
        switch (mode)
        {
            case FontMode.Set:
                ToSet(r, prop, now);
                break;
            case FontMode.Adjust:
                var up = Resolve(r.Parent);
                if (prop == FontProp.Size) { r.Size.Mode = mode; r.Size.Value = Math.Round(now.Size - up.Size, 4); }
                else { r.Weight.Mode = mode; r.Weight.Value = Math.Round((double)(now.Weight - up.Weight), 4); }
                break;
            default:
                if (prop == FontProp.Family) { r.Family.Mode = mode; }
                else if (prop == FontProp.Size) { r.Size.Mode = mode; }
                else { r.Weight.Mode = mode; }
                break;
        }
    }

    private static void ToSet(FontRule r, FontProp prop, ResolvedFont now)
    {
        switch (prop)
        {
            case FontProp.Family: r.Family.Mode = FontMode.Set; r.Family.Value = now.Family; break;
            case FontProp.Size: r.Size.Mode = FontMode.Set; r.Size.Value = now.Size; break;
            default: r.Weight.Mode = FontMode.Set; r.Weight.Value = now.Weight; break;
        }
    }

    /// <summary>Own size or weight: fitted to the limits before it is stored. When adjusting it is the offset. Ignored while linked.</summary>
    public void SetNumber(string key, FontProp prop, double value)
    {
        var r = rules[key];
        var f = prop == FontProp.Size ? r.Size : r.Weight;
        if (prop == FontProp.Family || r.Of(f) == FontMode.Link)
        {
            return;
        }

        f.Value = r.Of(f) == FontMode.Adjust ? Math.Round(Math.Clamp(value, -1000, 1000), 4)
            : prop == FontProp.Size ? FitSize(key, value) : FitWeight(key, value);
    }

    public void SetName(string key, string family)
    {
        var r = rules[key];
        var f = r.Family;
        if (r.Of(f) != FontMode.Link)
        {
            f.Value = family;
        }
    }

    /// <summary>Why these settings cannot be used, or null: an unknown parent, a loop, a link or adjust without a parent, or a value or limit that is not valid.</summary>
    public static string? Validate(IReadOnlyDictionary<string, FontRule> rules)
    {
        foreach (var (key, r) in rules)
        {
            if (r.Parent.Length > 0 && !rules.ContainsKey(r.Parent))
            {
                return $"\"{key}\": unknown parent \"{r.Parent}\"";
            }

            var linked = r.Of(r.Family) != FontMode.Set || r.Of(r.Size) != FontMode.Set || r.Of(r.Weight) != FontMode.Set;
            if (linked && r.Parent.Length == 0)
            {
                return $"\"{key}\": link or adjust without a parent";
            }

            if (r.Of(r.Family) == FontMode.Adjust)
            {
                return $"\"{key}\": a family cannot be adjusted";
            }

            if (Bad(r, r.Size, 1, 400) is { } size)
            {
                return $"\"{key}\" size: {size}";
            }

            if (Bad(r, r.Weight, 1, 1000) is { } weight)
            {
                return $"\"{key}\" weight: {weight}";
            }

            var seen = new HashSet<string>();
            for (var c = key; c.Length > 0; c = rules.TryGetValue(c, out var next) ? next.Parent : "")
            {
                if (!seen.Add(c))
                {
                    return $"Cycle at \"{c}\"";
                }
            }
        }

        return null;
    }

    private static string? Bad(FontRule r, FontNumberRule f, double lo, double hi)
    {
        if (f.Value is { } v && (!double.IsFinite(v) || (r.Of(f) == FontMode.Set && (v < lo || v > hi)) || (r.Of(f) == FontMode.Adjust && Math.Abs(v) > hi)))
        {
            return $"value {v} is not valid";
        }

        if (f.Limit is { } l)
        {
            if (l.Options is { } o && o.Any(x => !double.IsFinite(x) || x < 1 || x > 1000))
            {
                return "options must be between 1 and 1000";
            }

            if (l.Step is <= 0 || (l.Min is { } a && l.Max is { } b && a > b) || (l.Min is { } m && m < 0))
            {
                return "limit is not valid";
            }
        }

        return null;
    }
}
