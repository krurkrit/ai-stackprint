// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Which part of a font setting.</summary>
public enum FontProp { Family, Size, Weight }

/// <summary>
/// The font settings as a graph: each one may have a parent whose value it links to or adjusts. Resolving works out the family, size and weight of a setting
/// and fits them to that setting's own limits. Works on the dictionary in <see cref="Settings.Fonts"/> directly, so a change is a change of the settings.
/// </summary>
public sealed partial class FontRuleSet(Dictionary<string, FontRule> rules)
{
    private static readonly FontLimit _sizeLimit = new() { Min = 1, Max = 400, Step = 1 };
    private static readonly FontLimit _weightLimit = new() { Min = 100, Max = 900, Step = 100 };

    public IReadOnlyDictionary<string, FontRule> Rules => rules;

    public bool Has(string key) => rules.ContainsKey(key);

    public string Parent(string key) => rules.TryGetValue(key, out var r) ? r.Parent : "";

    public FontMode Mode(string key, FontProp prop) => !rules.TryGetValue(key, out var r) ? FontMode.Set : prop switch
    {
        FontProp.Family => r.Of(r.Family),
        FontProp.Size => r.Of(r.Size),
        _ => r.Of(r.Weight)
    };

    /// <summary>The stored value: the family name, or the number (a size, a weight, or the offset when adjusting).</summary>
    public double? Number(string key, FontProp prop) => !rules.TryGetValue(key, out var r) ? null : prop == FontProp.Size ? r.Size.Value : prop == FontProp.Weight ? r.Weight.Value : null;

    public string? Name(string key) => rules.TryGetValue(key, out var r) ? r.Family.Value : null;

    /// <summary>The limits that apply to this setting's size or weight: the setting's own, then the app's for the role, then the built-in ones, part by part.</summary>
    public FontLimit Limits(string key, FontProp prop)
    {
        var own = rules.TryGetValue(key, out var r) ? (prop == FontProp.Size ? r.Size.Limit : r.Weight.Limit) : null;
        var role = FontRoles.Find(key) is { } d ? (prop == FontProp.Size ? d.Size : d.Weight) : null;
        var basic = prop == FontProp.Size ? _sizeLimit : _weightLimit;
        var options = own?.Options ?? (own?.Min == null && own?.Max == null && own?.Step == null ? role?.Options : null);
        return new FontLimit
        {
            Min = own?.Min ?? role?.Min ?? basic.Min,
            Max = own?.Max ?? role?.Max ?? basic.Max,
            Step = own?.Step ?? role?.Step ?? basic.Step,
            Options = options
        };
    }

    /// <summary>Clamps to min–max, then snaps to the step grid that starts at min.</summary>
    public double FitSize(string key, double value)
    {
        var l = Limits(key, FontProp.Size);
        var min = l.Min!.Value;
        var max = Math.Max(min, l.Max!.Value);
        var step = l.Step is > 0 ? l.Step.Value : 1;
        var v = Math.Clamp(double.IsFinite(value) ? value : min, min, max);
        v = min + Math.Round((v - min) / step, MidpointRounding.AwayFromZero) * step;
        return Math.Round(Math.Min(v, max), 4);
    }

    /// <summary>The allowed weights of this setting, lowest first.</summary>
    public IReadOnlyList<int> Weights(string key)
    {
        var l = Limits(key, FontProp.Weight);
        if (l.Options is { Length: > 0 } o)
        {
            return [.. o.Select(v => (int)Math.Round(v)).Where(v => v is >= 1 and <= 1000).Distinct().Order()];
        }

        var list = new List<int>();
        var step = l.Step is > 0 ? l.Step.Value : 100;
        for (var v = l.Min!.Value; v <= l.Max!.Value + 1e-9 && list.Count < 1000; v += step)
        {
            list.Add((int)Math.Round(Math.Clamp(v, 1, 1000)));
        }

        return list.Count > 0 ? [.. list.Distinct()] : [400];
    }

    /// <summary>The nearest allowed weight; on a tie it goes the way of <paramref name="direction"/> (the sign of an offset; 0 goes up).</summary>
    public int FitWeight(string key, double value, int direction = 0)
    {
        var all = Weights(key);
        var best = all[0];
        foreach (var w in all)
        {
            var d = Math.Abs(w - value);
            var bd = Math.Abs(best - value);
            if (d < bd - 1e-9 || (Math.Abs(d - bd) <= 1e-9 && (direction >= 0 ? w > best : w < best)))
            {
                best = w;
            }
        }

        return best;
    }

    /// <summary>Family, size and weight of this setting; every part fitted to its own limits. Follows parents; a loop is cut where it comes back.</summary>
    public ResolvedFont Resolve(string key) => ResolveCore(key, []);

    private ResolvedFont ResolveCore(string key, HashSet<string> seen)
    {
        var fallback = FontRoles.Find(key)?.DefaultFamily ?? "Inter";
        if (!rules.TryGetValue(key, out var r) || !seen.Add(key))
        {
            return new ResolvedFont(fallback, 14, 400, 14, 400);
        }

        var parent = r.Parent.Length > 0 && rules.ContainsKey(r.Parent) ? ResolveCore(r.Parent, seen) : (ResolvedFont?)null;
        seen.Remove(key);
        var family = r.Of(r.Family) == FontMode.Link && parent is { } pf ? pf.Family : string.IsNullOrWhiteSpace(r.Family.Value) ? fallback : r.Family.Value;
        var rawSize = RawOf(r.Of(r.Size), r.Size, parent?.Size, 14);
        var rawWeight = RawOf(r.Of(r.Weight), r.Weight, parent?.Weight, 400);
        var dir = r.Of(r.Weight) == FontMode.Adjust ? Math.Sign(r.Weight.Value ?? 0) : 0;
        return new ResolvedFont(family, FitSize(key, rawSize), FitWeight(key, rawWeight, dir), rawSize, rawWeight);
    }

    private static double RawOf(FontMode mode, FontNumberRule f, double? parent, double fallback) => mode switch
    {
        FontMode.Link when parent != null => parent.Value,
        FontMode.Adjust when parent != null => parent.Value + (f.Value ?? 0),
        _ => f.Value ?? fallback
    };

    /// <summary>For the tooltip of an adjusted field: <c>Body Normal (400) +200 = 600 → nearest allowed Bold (700)</c>. Empty when the field is not adjusted.</summary>
    public string Describe(string key, FontProp prop)
    {
        if (prop == FontProp.Family || !rules.TryGetValue(key, out var r) || r.Parent.Length == 0 || !rules.ContainsKey(r.Parent))
        {
            return "";
        }

        var f = prop == FontProp.Size ? r.Size : r.Weight;
        if (r.Of(f) != FontMode.Adjust)
        {
            return "";
        }

        var p = Resolve(r.Parent);
        var label = FontRoles.Label(r.Parent);
        var offset = f.Value ?? 0;
        var sign = offset >= 0 ? "+" : "-";
        var o = Math.Abs(offset);
        var me = Resolve(key);
        if (prop == FontProp.Size)
        {
            var raw = p.Size + offset;
            return $"{label} {p.Size:0.##}px {sign}{o:0.##} = {raw:0.##}px" + (Math.Abs(raw - me.Size) > 1e-6 ? $" → nearest allowed {me.Size:0.##}px" : "");
        }

        var rawW = p.Weight + offset;
        return $"{label} {FontWeights.Label(p.Weight)} {sign}{o:0.##} = {rawW:0.##}" + (Math.Abs(rawW - me.Weight) > 1e-6 ? $" → nearest allowed {FontWeights.Label(me.Weight)}" : "");
    }
}
