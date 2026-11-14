// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>The project's config files (JSON): secrets, MCP servers, hooks and permission settings. Values that look secret are never shown.</summary>
public static partial class ProjectChecks
{
    private static readonly JsonDocumentOptions _jsonc = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly string[] _serverKeys = ["mcpServers", "servers", "mcp", "context_servers"];   // Claude, Cursor, Gemini, Kiro, Qwen / VS Code / OpenCode / Zed
    private static readonly string[] _runners = ["npx", "npx.cmd", "uvx", "bunx", "pnpx"];
    public const string Hidden = "•••";

    private static void Configs(string project, IReadOnlyList<AppDefinition> defs, List<ProjectIssue> issues, List<ProjectProgram> programs)
    {
        var files = defs.SelectMany(d => d.Configs.Where(c => Scanner.InProject(c.Level) && c.Kind is "mcp" or "settings" && PathUtil.OsMatches(c.Os)).Select(c => (App: d, Config: c)))
            .GroupBy(x => x.Config.Path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        foreach (var g in files)
        {
            var path = Path.Combine(project, g.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || !Path.GetExtension(path).StartsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(ContextBudget.ReadText(path), _jsonc); }
            catch (JsonException e) { issues.Add(new([.. g.Select(x => x.App.Id).Distinct()], path, new($"{g.Key} is not valid JSON (line {e.LineNumber + 1}): the app may ignore it."))); continue; }

            using (doc)
            {
                var apps = g.Select(x => x.App.Id).Distinct().ToList();
                var local = g.Any(x => x.Config.Level == "Local");
                Secrets(doc.RootElement, "", g.Key, path, apps, local, issues);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    Servers(doc.RootElement, path, programs);
                    Hooks(doc.RootElement, path, programs);
                    Permissions(doc.RootElement, g.Key, path, apps, issues);
                }
            }
        }

        foreach (var p in programs.Where(p => p.Note?.StartsWith("Not pinned", StringComparison.Ordinal) == true))
        {
            issues.Add(new([], p.File, new($"MCP server \"{p.Name}\" in {Rel(project, p.File)} runs a package without a fixed version: each start can fetch a newer, unreviewed release.", false)));
        }
    }

    /// <summary>String values that look like keys or tokens, by their name (api_key, token, …) or their shape (sk-…, ghp_…).</summary>
    private static void Secrets(JsonElement e, string at, string rel, string path, List<string> apps, bool local, List<ProjectIssue> issues)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String && LooksSecret(p.Name, p.Value.GetString()!))
                    {
                        issues.Add(new(apps, path, new($"{rel}: \"{at}{p.Name}\" holds what looks like a secret (value hidden). " + (local
                            ? "This personal file is usually not committed, but the key is stored in plain text."
                            : "Files in a project are often committed: read it from an environment variable instead."), !local)));
                    }
                    else
                    {
                        Secrets(p.Value, at + p.Name + ".", rel, path, apps, local, issues);
                    }
                }

                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var v in e.EnumerateArray())
                {
                    if (v.ValueKind == JsonValueKind.String && TokenShape().IsMatch(v.GetString()!))
                    {
                        issues.Add(new(apps, path, new($"{rel}: \"{at.TrimEnd('.')}[{i}]\" holds what looks like a secret (value hidden).", !local)));
                    }
                    else
                    {
                        Secrets(v, $"{at.TrimEnd('.')}[{i}].", rel, path, apps, local, issues);
                    }

                    i++;
                }

                break;
            default:
                break;
        }
    }

    /// <summary>A literal secret: not a reference such as ${VAR}, {env:VAR} or $VAR, not a placeholder.</summary>
    internal static bool LooksSecret(string name, string value)
    {
        if (TokenShape().IsMatch(value))
        {
            return true;
        }

        var v = value.Trim();
        return SecretName().IsMatch(name) && v.Length >= 16 && !v.Contains('$') && !v.Contains("{env:", StringComparison.OrdinalIgnoreCase)
            && !v.Contains(' ') && !v.StartsWith('<') && !Placeholder().IsMatch(v);
    }

    /// <summary>MCP servers: command and arguments (secret-looking ones hidden), or the address of a remote server.</summary>
    private static void Servers(JsonElement root, string path, List<ProjectProgram> programs)
    {
        foreach (var key in _serverKeys)
        {
            if (!root.TryGetProperty(key, out var servers) || servers.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var s in servers.EnumerateObject().Where(s => s.Value.ValueKind == JsonValueKind.Object))
            {
                var v = s.Value;
                var parts = Str(v, "command") is { } c ? [c, .. Strings(v, "args")] : Strings(v, "command");   // OpenCode: "command": ["npx", "x"]
                var url = Str(v, "url") ?? Str(v, "httpUrl") ?? Str(v, "serverUrl");
                if (parts.Count == 0 && url == null)
                {
                    continue;
                }

                var notes = new List<string>();
                if (parts.Count > 0 && Unpinned(parts) is { } pkg)
                {
                    notes.Add("Not pinned: " + pkg);
                }

                if (v.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object && env.EnumerateObject().Any())
                {
                    notes.Add("env: " + string.Join(", ", env.EnumerateObject().Select(p => p.Name)));
                }

                if (v.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True || v.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False)
                {
                    notes.Add("disabled");
                }

                programs.Add(new("MCP server", s.Name, parts.Count > 0 ? CommandLine(parts) : Mask(url!), path, notes.Count > 0 ? string.Join(" · ", notes) : null));
            }
        }
    }

    /// <summary>Hooks: every "command" under a top-level "hooks" object, named by its event (and matcher).</summary>
    private static void Hooks(JsonElement root, string path, List<ProjectProgram> programs)
    {
        if (!root.TryGetProperty("hooks", out var hooks) || hooks.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var ev in hooks.EnumerateObject())
        {
            Walk(ev.Value, ev.Name);
        }

        void Walk(JsonElement e, string name)
        {
            if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in e.EnumerateArray())
                {
                    Walk(x, name);
                }
            }
            else if (e.ValueKind == JsonValueKind.Object)
            {
                var n = Str(e, "matcher") is { Length: > 0 } m ? $"{name} · {m}" : name;
                if (Str(e, "command") is { } cmd)
                {
                    programs.Add(new("Hook", n, Mask(cmd), path, Str(e, "type") is { } t && t != "command" ? t : null));
                }

                foreach (var p in e.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object))
                {
                    Walk(p.Value, n);
                }
            }
        }
    }

    /// <summary>Claude Code settings that skip permission prompts.</summary>
    private static void Permissions(JsonElement root, string rel, string path, List<string> apps, List<ProjectIssue> issues)
    {
        if (!root.TryGetProperty("permissions", out var perm) || perm.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (Str(perm, "defaultMode") == "bypassPermissions")
        {
            issues.Add(new(apps, path, new($"{rel} sets defaultMode to bypassPermissions: every action runs without asking.")));
        }

        foreach (var rule in Strings(perm, "allow").Where(r => r.Replace(" ", "") is "Bash" or "Bash(*)" or "Bash(*:*)" or "Bash(:*)"))
        {
            issues.Add(new(apps, path, new($"{rel} allows \"{rule}\": every shell command runs without asking.")));
        }
    }

    /// <summary>The package a runner (npx, uvx, bunx, pnpx) or docker starts without a fixed version, or null.</summary>
    internal static string? Unpinned(IReadOnlyList<string> parts)
    {
        var exe = Path.GetFileName(parts[0]).ToLowerInvariant();
        var args = parts.Skip(1).ToList();
        if (exe is "cmd" or "cmd.exe" && args.Count > 1 && args[0].Equals("/c", StringComparison.OrdinalIgnoreCase))
        {
            return Unpinned(args[1..]);   // Windows: cmd /c npx …
        }

        if (_runners.Contains(exe) || (exe is "pnpm" or "pnpm.cmd" && args.FirstOrDefault() == "dlx"))
        {
            var pkg = args.Where(a => a != "dlx").SkipWhile(a => a.StartsWith('-')).FirstOrDefault();
            var pinned = pkg == null || pkg.Contains("==") || (pkg.LastIndexOf('@') > 0 && !pkg.EndsWith("@latest", StringComparison.OrdinalIgnoreCase)) || pkg.StartsWith('.') || Path.IsPathRooted(pkg);
            return pinned ? null : pkg;
        }

        if (exe is "docker" or "docker.exe" or "podman" && args.FirstOrDefault() == "run")
        {
            var image = args.Skip(1).Where((a, i) => !a.StartsWith('-') && (i == 0 || !Flag().IsMatch(args[i]))).FirstOrDefault();
            return image == null || image.Contains('@') || (image.Contains(':') && !image.EndsWith(":latest", StringComparison.Ordinal)) ? null : image;
        }

        return null;
    }

    /// <summary>A command line to show: arguments that look secret, or follow a --token / --api-key style flag, are hidden.</summary>
    private static string CommandLine(List<string> parts) =>
        string.Join(" ", parts.Select((p, i) => i > 0 && (SecretFlag().IsMatch(parts[i - 1]) || TokenShape().IsMatch(p)) ? Hidden : Mask(p)));

    private static string Mask(string s) => SecretAssign().Replace(TokenShape().Replace(s, Hidden), m => m.Groups[1].Value + Hidden);

    private static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
        ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)] : [];

    [GeneratedRegex(@"\b(?:sk-(?:ant-|proj-)?[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|xox[abprs]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35}|glpat-[A-Za-z0-9_-]{20,}|npm_[A-Za-z0-9]{36}|hf_[A-Za-z0-9]{30,})")]
    private static partial Regex TokenShape();
    [GeneratedRegex(@"(?i)(api[_-]?key|apikey|token|secret|password|passwd|auth|credential|private[_-]?key)")] private static partial Regex SecretName();
    [GeneratedRegex(@"(?i)^(x{3,}|\.{3,}|\*{3,}|your[_-]|changeme|placeholder|example|<)")] private static partial Regex Placeholder();
    [GeneratedRegex(@"(?i)^--?(api[_-]?key|token|secret|password|auth)")] private static partial Regex SecretFlag();
    [GeneratedRegex(@"(?i)((?:api[_-]?key|token|secret|password)=)[^\s&""']+")] private static partial Regex SecretAssign();
    [GeneratedRegex(@"^-(e|v|p|w|u|-env|-volume|-publish|-name|-network|-workdir|-user|-entrypoint)$")] private static partial Regex Flag();
}
