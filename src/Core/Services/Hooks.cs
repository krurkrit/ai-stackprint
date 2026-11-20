// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

using static AiStackprint.Services.ConfigJson;

namespace AiStackprint.Services;

/// <summary>A hook: a command (or a prompt) an app runs on an event, from a settings file, a hook file or a plugin. The command is shown masked.</summary>
/// <param name="Matcher">The tools or files it applies to; null = every one.</param>
/// <param name="What">The command line with secret-looking parts hidden, or the prompt's first line.</param>
/// <param name="Type">"command" or "prompt".</param>
public sealed record Hook(string Event, string? Matcher, string What, string Type, string File)
{
    public IReadOnlyList<string> Apps { get; init; } = [];
    public string Level { get; init; } = "User";
    public string? LevelLabel { get; init; }
    public string? Project { get; init; }
    /// <summary>The plugin that adds it; null when a settings or hook file names it.</summary>
    public string? Plugin { get; init; }
    /// <summary>The script it runs when that does not exist (masked), else null.</summary>
    public string? Missing { get; init; }
    public string? Note { get; init; }
    /// <summary>It runs a missing script, or downloads and runs code in one step.</summary>
    public bool Warn { get; init; }
    /// <summary>SHA-256 of its definition as written: a change means the command or its matcher changed. The text itself is not kept.</summary>
    public string Fingerprint { get; init; } = "";
    public string Name => Matcher is { Length: > 0 } m && m != "*" ? $"{Event} · {m}" : Event;
}

/// <summary>
/// Hooks of every app, read-only. One JSON shape covers Claude Code and Gemini CLI settings (<c>hooks: { Event: [{ matcher, hooks: [{ type, command }] }] }</c>),
/// Claude plugins' hooks/hooks.json, Cursor's hooks.json and Copilot's .github/hooks/*.json (<c>{ version, hooks: { event: [{ command | bash | powershell }] }}</c>).
/// Kiro's hook files (.kiro.hook and the newer .json) are read best effort: their format changed in Kiro 1.0.
/// </summary>
public static partial class Hooks
{
    private const int MaxPrompt = 80;

    /// <summary>Every hook in the settings and hook files of a scan (a hook file entry may be a folder of files).</summary>
    public static List<Hook> Read(IEnumerable<Item> items, string home)
    {
        var res = new List<Hook>();
        foreach (var g in items.Where(i => i.Kind == "Config" && i.Description is "Settings" or "Hooks").GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
        {
            var first = g.First();
            var files = Directory.Exists(g.Key) ? Files(g.Key) : [g.Key];
            foreach (var f in files)
            {
                // where relative script paths start: the project, a user hook file's own folder (Cursor runs ~/.cursor/hooks.json from ~/.cursor), else home
                var from = first.Project ?? (first.Description == "Hooks" ? Path.GetDirectoryName(f)! : home);
                res.AddRange(InFile(f, from, home).Select(h => h with
                {
                    Apps = [.. g.Select(i => i.App).Distinct()],
                    Level = first.Level,
                    LevelLabel = first.LevelLabel,
                    Project = first.Project
                }));
            }
        }

        return res;
    }

    /// <summary>The hooks plugins add: hooks/hooks.json and what the manifest's "hooks" names (a path inside the plugin, or the hooks themselves).</summary>
    public static List<Hook> FromPlugins(IEnumerable<Item> items, string home)
    {
        var res = new List<Hook>();
        foreach (var p in items.Where(i => i.Kind == "Plugin" && i.Path != "" && Directory.Exists(i.Path)))
        {
            res.AddRange(InPlugin(p.Path, home).Select(h => h with
            {
                Apps = [p.App],
                Level = p.Level,
                LevelLabel = p.LevelLabel,
                Project = p.Project,
                Plugin = p.Name,
                Note = p.Scope == "disabled" ? Join(h.Note, "plugin disabled") : h.Note,
            }));
        }

        return res;
    }

    internal static List<Hook> InPlugin(string folder, string home)
    {
        var res = new List<Hook>();
        var read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void FromFile(string path)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full) && ContextBudget.Inside(full, folder) && read.Add(full))
            {
                res.AddRange(InFile(full, folder, home, folder));
            }
        }

        FromFile(Path.Combine(folder, "hooks", "hooks.json"));
        foreach (var path in PluginContents.Named(PluginManifests.Find(folder)).GetValueOrDefault("hooks", []))
        {
            FromFile(Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar)));
        }

        if (PluginManifests.Find(folder) is { } manifest && manifest.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Doc(manifest) is { } doc)
        {
            using (doc)
            {
                res.AddRange(Parse(doc.RootElement, manifest, folder, home, folder));   // inline "hooks": { Event: … }
            }
        }

        return res;
    }

    /// <summary>The hooks in one file: JSON with a top-level "hooks" object, or a Kiro hook file. None when it cannot be read.</summary>
    /// <param name="from">Where relative script paths start.</param>
    /// <param name="pluginRoot">The plugin folder, for ${CLAUDE_PLUGIN_ROOT}.</param>
    internal static List<Hook> InFile(string path, string from, string home, string? pluginRoot = null)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".json" or ".jsonc" or ".hook") || Doc(path) is not { } doc)
        {
            return [];
        }

        using (doc)
        {
            return ext == ".hook" || path.Contains(Path.Combine(".kiro", "hooks"), StringComparison.OrdinalIgnoreCase)
                ? Kiro(doc.RootElement, path, from, home) is { } k ? [k] : []
                : IsAntigravity(doc.RootElement) ? Antigravity(doc.RootElement, path, from, home)
                : Parse(doc.RootElement, path, from, home, pluginRoot);
        }
    }

    /// <summary>Every command or prompt under a top-level "hooks" object, with its event and matcher.</summary>
    internal static List<Hook> Parse(JsonElement root, string path, string from, string home, string? pluginRoot = null)
    {
        var res = new List<Hook>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("hooks", out var hooks) || hooks.ValueKind != JsonValueKind.Object)
        {
            return res;
        }

        foreach (var ev in hooks.EnumerateObject())
        {
            Walk(ev.Value, ev.Name, null);
        }

        return res;

        void Walk(JsonElement e, string ev, string? matcher)
        {
            if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in e.EnumerateArray())
                {
                    Walk(x, ev, matcher);
                }
            }
            else if (e.ValueKind == JsonValueKind.Object)
            {
                matcher = Str(e, "matcher") ?? matcher;
                if (Make(ev, matcher, e, path, from, home, pluginRoot) is { } h)
                {
                    res.Add(h);
                }

                if (e.TryGetProperty("hooks", out var inner))
                {
                    Walk(inner, ev, matcher);
                }
            }
        }
    }

    private static Hook? Make(string ev, string? matcher, JsonElement e, string path, string from, string home, string? pluginRoot)
    {
        // Copilot: bash and powershell variants; the one this system runs first
        var cmd = Str(e, "command") ?? (OperatingSystem.IsWindows() ? Str(e, "powershell") ?? Str(e, "bash") : Str(e, "bash") ?? Str(e, "powershell")) ?? Str(e, "exec");
        var prompt = Str(e, "prompt");
        if (cmd == null && prompt == null)
        {
            return null;
        }

        var h = cmd != null
            ? Checked(new Hook(ev, matcher, Mask(cmd), "command", path), cmd, from, home, pluginRoot)
            : new Hook(ev, matcher, FirstLine(prompt!), "prompt", path);
        return h with { Fingerprint = Services.Fingerprint.Text(e.GetRawText()) };
    }

    /// <summary>A missing script, or download-and-run in one step: noted and a warning.</summary>
    private static Hook Checked(Hook h, string cmd, string from, string home, string? pluginRoot)
    {
        if (pluginRoot != null)
        {
            cmd = cmd.Replace("${CLAUDE_PLUGIN_ROOT}", pluginRoot, StringComparison.Ordinal).Replace("$CLAUDE_PLUGIN_ROOT", pluginRoot, StringComparison.Ordinal);
        }

        var missing = HookScript.Missing(cmd, from, home) is { } m ? Mask(m) : null;
        var note = missing != null ? $"runs {missing}, which does not exist" : null;
        if (SafetyScan.DownloadsAndRuns(cmd))
        {
            note = Join(note, "downloads and runs code in one step");
        }

        return h with
        {
            Missing = missing,
            Note = note,
            Warn = note != null
        };
    }

    private static string FirstLine(string s)
    {
        var line = s.Trim().Split('\n')[0].Trim();
        return "Prompt: " + (line.Length > MaxPrompt ? line[..MaxPrompt] + "…" : line);
    }

    internal static string? Join(string? a, string b) => a == null ? b : $"{a} · {b}";

    internal static IEnumerable<string> Files(string folder)
    {
        try { return Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".kiro.hook", StringComparison.OrdinalIgnoreCase)).Order().ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private static JsonDocument? Doc(string path)
    {
        try { return JsonDocument.Parse(ContextBudget.ReadText(path), Jsonc); }
        catch (JsonException) { return null; }
    }
}
