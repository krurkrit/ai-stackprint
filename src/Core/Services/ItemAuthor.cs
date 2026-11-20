// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Who made a skill or plugin, as the dashboard counts it and search filters it: the plugin manifest's author, the skill's author or
/// metadata.author, else the plugin's marketplace. A plugin's skills count for the plugin's author. Skills with none are "Local" (yours)
/// or the app's name when they ship with it.
/// </summary>
public static class ItemAuthor
{
    internal const string Local = "Local";

    /// <param name="all">Every scanned item, so a plugin's skills find the plugin's author.</param>
    public static Func<Item, string> For(IEnumerable<Item> all)
    {
        var plugins = new Dictionary<(string App, string Name), string>();
        foreach (var p in all.Where(i => i.Kind == "Plugin" && i.Author != null))
        {
            plugins.TryAdd((p.App, p.Name), p.Author!);
        }

        return i => i.Author ?? (i.Plugin is { } p
            ? plugins.GetValueOrDefault((i.App, p)) ?? (p.Contains('@') ? p[(p.IndexOf('@') + 1)..] : p)
            : i.Bundled ? i.Source : Local);
    }
}
