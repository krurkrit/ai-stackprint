// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>The values the skill filters offer (UI-free, so they can be tested).</summary>
public static class SkillMatch
{
    /// <summary>Who can start the skill: "Agent only", "User only" or "Agent and User"; "" for items without invokers (plugins).</summary>
    public static string Label(Item i) => i.Invokers.Count switch { 0 => "", 1 => i.Invokers[0] + " only", _ => string.Join(" and ", i.Invokers) };

    /// <summary>Where a skill came from: its plugin, the app itself (bundled) or a skills folder.</summary>
    public static string From(Item i) => i.Plugin?.Split('@')[0] ?? (i.Bundled ? "Bundled" : "Folder");
}
