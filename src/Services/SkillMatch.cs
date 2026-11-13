using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>The skill and plugin filter rules (UI-free, so they can be tested).</summary>
public static class SkillMatch
{
    /// <summary>Level checked; invoked by exactly <paramref name="invoked"/> ("" = all, else a <see cref="Label"/>); source matches ("" = any).</summary>
    public static bool Shows(Item i, IReadOnlySet<string> offLevels, string invoked, string from) =>
        !offLevels.Contains(i.Level) && (invoked == "" || Label(i) == invoked) && (from == "" || From(i) == from);

    /// <summary>Who can start the skill: "Agent only", "User only" or "Agent and User"; "" for items without invokers (plugins).</summary>
    public static string Label(Item i) => i.Invokers.Count switch { 0 => "", 1 => i.Invokers[0] + " only", _ => string.Join(" and ", i.Invokers) };

    /// <summary>Where a skill came from: its plugin, the app itself (bundled) or a skills folder.</summary>
    public static string From(Item i) => i.Plugin?.Split('@')[0] ?? (i.Bundled ? "Bundled" : "Folder");
}
