// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using static AiStackprint.Services.ConfigJson;

namespace AiStackprint.Services;

public static partial class Hooks
{
    /// <summary>
    /// One Kiro hook file, best effort: the older .kiro.hook (<c>when: { type, patterns }, then: { type: askAgent | runCommand, prompt | command }</c>) or the
    /// newer .json (<c>trigger</c>/<c>event</c>, <c>matcher</c>, <c>action: { type, prompt | command }</c>). Disabled hooks are noted.
    /// </summary>
    private static Hook? Kiro(JsonElement r, string path, string from, string home)
    {
        if (r.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var when = Obj(r, "when");
        var then = Obj(r, "then") ?? Obj(r, "action");
        if (then is not { } act)
        {
            return null;
        }

        var ev = (when is { } w ? Str(w, "type") : null) ?? Str(r, "trigger") ?? Str(r, "event") ?? Str(r, "name") ?? Path.GetFileNameWithoutExtension(path);
        var matcher = when is { } w2 && w2.TryGetProperty("patterns", out var pats) && pats.ValueKind == JsonValueKind.Array
            ? string.Join(", ", pats.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()))
            : Str(r, "matcher");
        var cmd = Str(act, "command");
        var prompt = Str(act, "prompt");
        if (cmd == null && prompt == null)
        {
            return null;
        }

        var h = cmd != null ? Checked(new Hook(ev, matcher is "" ? null : matcher, Mask(cmd), "command", path), cmd, from, home, null)
            : new Hook(ev, matcher is "" ? null : matcher, FirstLine(prompt!), "prompt", path);
        if (r.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False)
        {
            h = h with { Note = Join(h.Note, "disabled") };
        }

        return h with { Fingerprint = Services.Fingerprint.Text(r.GetRawText()) };
    }

    private static JsonElement? Obj(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
}
