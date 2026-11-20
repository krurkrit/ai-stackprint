// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using static AiStackprint.Services.ConfigJson;

namespace AiStackprint.Services;

public static partial class Hooks
{
    private static readonly string[] _antigravityEvents = ["SessionStart", "PreToolUse", "PostToolUse", "PreInvocation", "PostInvocation", "Stop"];

    /// <summary>
    /// Antigravity CLI's hooks.json (~/.gemini/config/hooks.json, .agents/hooks.json): no top-level "hooks" key; each key is a hook name
    /// holding its events and an optional "enabled". Tool events hold matcher groups (<c>{ matcher, hooks: [{ type, command, timeout }] }</c>),
    /// the others a plain list of handlers. A hook switched off with <c>"enabled": false</c> is listed with a note.
    /// </summary>
    internal static List<Hook> Antigravity(JsonElement root, string path, string from, string home)
    {
        var res = new List<Hook>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return res;
        }

        foreach (var named in root.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
        {
            var off = named.Value.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.False;
            foreach (var ev in named.Value.EnumerateObject().Where(p => _antigravityEvents.Contains(p.Name, StringComparer.Ordinal)))
            {
                Walk(ev.Value, ev.Name, null);
            }

            void Walk(JsonElement el, string eventName, string? matcher)
            {
                if (el.ValueKind == JsonValueKind.Array)
                {
                    foreach (var x in el.EnumerateArray())
                    {
                        Walk(x, eventName, matcher);
                    }
                }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    matcher = Str(el, "matcher") ?? matcher;
                    if (Make(eventName, matcher, el, path, from, home, null) is { } h)
                    {
                        res.Add(h with { Note = Join(h.Note, off ? $"{named.Name}: switched off" : $"hook {named.Name}") });
                    }

                    if (el.TryGetProperty("hooks", out var inner))
                    {
                        Walk(inner, eventName, matcher);
                    }
                }
            }
        }

        return res;
    }

    /// <summary>Whether a JSON file has Antigravity's layout: no "hooks" key, and an object that holds one of its events.</summary>
    private static bool IsAntigravity(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("hooks", out _)
        && root.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.Object && p.Value.EnumerateObject().Any(q => _antigravityEvents.Contains(q.Name, StringComparer.Ordinal)));
}
