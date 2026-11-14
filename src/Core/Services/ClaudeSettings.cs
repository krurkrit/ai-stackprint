// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

using static AiStackprint.Services.ConfigJson;

namespace AiStackprint.Services;

/// <param name="Effect">allow, ask or deny.</param>
/// <param name="Note">Why it has no effect, or where else it is set; null when nothing to say.</param>
public record ClaudeRule(string Effect, string Rule, string Level, string File, string? Note);

/// <param name="Overrides">The lower-priority levels that set the same key and lose; null when none.</param>
public record ClaudeSetting(string Key, string Value, string Level, string File, string? Overrides);

public record ClaudeSettingsInfo(List<ClaudeRule> Rules, List<ClaudeSetting> Settings)
{
    public bool Any => Rules.Count > 0 || Settings.Count > 0;
}

/// <summary>
/// Claude Code's settings files for one project, read in precedence order (managed, local, project, user): permission rules
/// with the effect a deny elsewhere has, and the single settings with the level that wins. Secret-looking values are hidden; env shows names only.
/// </summary>
public static class ClaudeSettings
{
    private static readonly string[] _levels = ["System", "Local", "Project", "User"];   // highest priority first, as Levels.Order in the UI
    private static readonly string[] _effects = ["deny", "ask", "allow"];
    private static readonly string[] _scalars = ["defaultMode", "model", "enableAllProjectMcpServers"];
    private static readonly string[] _lists = ["additionalDirectories", "enabledMcpjsonServers", "disabledMcpjsonServers"];

    public static ClaudeSettingsInfo Read(IEnumerable<AppDefinition> defs, string project, string home)
    {
        var files = new List<(string Level, string Path, JsonElement Root, JsonDocument Doc)>();
        var configs = defs.Where(d => d.Id == "claude-code").SelectMany(d => d.Configs).Where(c => c.Kind == "settings" && PathUtil.OsMatches(c.Os))
            .OrderBy(c => Array.IndexOf(_levels, c.Level));
        foreach (var c in configs)
        {
            var path = Scanner.InProject(c.Level) ? System.IO.Path.Combine(project, c.Path.Replace('/', System.IO.Path.DirectorySeparatorChar))
                : c.Path.StartsWith("~/", StringComparison.Ordinal) ? System.IO.Path.Combine(home, c.Path[2..]) : PathUtil.Expand(c.Path);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var doc = JsonDocument.Parse(ContextBudget.ReadText(path), Jsonc);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    files.Add((c.Label ?? c.Level, path, doc.RootElement, doc));
                }
                else
                {
                    doc.Dispose();
                }
            }
            catch (JsonException) { }
        }

        try
        {
            return new(Rules(files), Settings(files));
        }
        finally
        {
            foreach (var f in files)
            {
                f.Doc.Dispose();
            }
        }
    }

    private static List<ClaudeRule> Rules(List<(string Level, string Path, JsonElement Root, JsonDocument Doc)> files)
    {
        var all = new List<(string Effect, string Rule, string Level, string File)>();
        foreach (var f in files.Where(f => f.Root.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Object))
        {
            var perm = f.Root.GetProperty("permissions");
            all.AddRange(_effects.SelectMany(e => Strings(perm, e).Select(r => (e, r, f.Level, f.Path))));
        }

        return [.. all.Select(r =>
        {
            string? note = null;
            var same = all.Where(o => o.Rule == r.Rule && o != r).ToList();
            if (r.Effect != "deny" && same.FirstOrDefault(o => o.Effect == "deny") is { Level: { } dl })
            {
                note = $"Never applies: denied in {dl}";
            }
            else if (r.Effect == "allow" && same.FirstOrDefault(o => o.Effect == "ask") is { Level: { } al })
            {
                note = $"Still asks: set to ask in {al}";
            }
            else if (same.Any(o => o.Effect == r.Effect))
            {
                note = "Also set in " + string.Join(", ", same.Where(o => o.Effect == r.Effect).Select(o => o.Level).Distinct());
            }

            return new ClaudeRule(r.Effect, Mask(r.Rule), r.Level, r.File, note);
        }).OrderBy(r => Array.IndexOf(_effects, r.Effect)).ThenBy(r => r.Rule, StringComparer.OrdinalIgnoreCase)];
    }

    private static List<ClaudeSetting> Settings(List<(string Level, string Path, JsonElement Root, JsonDocument Doc)> files)
    {
        var res = new List<ClaudeSetting>();
        foreach (var key in _scalars)
        {
            var set = files.Select(f => (f.Level, f.Path, V: Value(f.Root, key))).Where(x => x.V != null).ToList();
            if (set.Count > 0)
            {
                var (level, path, v) = set[0];
                res.Add(new(key, Mask(v!), level, path, set.Count > 1 ? string.Join(", ", set.Skip(1).Select(x => $"{x.Level}: {Mask(x.V!)}")) : null));
            }
        }

        foreach (var key in _lists)
        {
            foreach (var f in files)
            {
                var list = (f.Root.TryGetProperty(key, out _) ? Strings(f.Root, key) : f.Root.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Object ? Strings(p, key) : []).ToList();
                if (list.Count > 0)
                {
                    res.Add(new(key, string.Join(", ", list.Select(Mask)), f.Level, f.Path, null));
                }
            }
        }

        foreach (var f in files)
        {
            if (f.Root.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
            {
                res.Add(new("env", string.Join(", ", env.EnumerateObject().Select(p => p.Name)), f.Level, f.Path, null));
            }
        }

        return res;
    }

    /// <summary>A scalar setting as text, looked up at the top level or in "permissions".</summary>
    private static string? Value(JsonElement root, string key)
    {
        JsonElement v;
        if (!root.TryGetProperty(key, out v) && !(root.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out v)))
        {
            return null;
        }

        return v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Number => v.GetRawText(), _ => null };
    }
}
