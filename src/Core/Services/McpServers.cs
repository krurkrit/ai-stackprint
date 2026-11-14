// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;

using AiStackprint.Models;

using static AiStackprint.Services.ConfigJson;

namespace AiStackprint.Services;

/// <summary>An MCP server an app's config file starts or connects to.</summary>
/// <param name="What">The command line (secret-looking parts hidden) or the server address.</param>
/// <param name="Note">Not pinned, key in plain text, env names, disabled; null when there is nothing to say.</param>
/// <param name="Warn">It runs a package without a fixed version, or a key is stored in plain text.</param>
public sealed record McpServer(string Name, string What, string File, string? Note, bool Warn)
{
    public IReadOnlyList<string> Apps { get; init; } = [];
    public string Level { get; init; } = "User";
    public string? LevelLabel { get; init; }
    /// <summary>The project folder: for Project and Local files, and for servers an app keeps per project in a user file (~/.claude.json).</summary>
    public string? Project { get; init; }
    /// <summary>SHA-256 of the server's definition as written in the file: a change means its command, address, arguments or environment changed. The text itself is not kept.</summary>
    public string Fingerprint { get; init; } = "";
}

/// <summary>
/// MCP servers in the apps' config files (the scan's settings and MCP files): mcpServers (Claude Code and Desktop, Cursor, Gemini, Qwen, Kiro,
/// Windsurf, Copilot CLI, Roo), servers (VS Code), mcp (OpenCode), context_servers (Zed), per-project mcpServers under "projects" (Claude Code's
/// ~/.claude.json: local scope), and Codex's [mcp_servers.*] in config.toml. YAML files (Goose, Continue) are not read. Read-only.
/// </summary>
public static partial class McpServers
{
    private static readonly string[] _keys = ["mcpServers", "servers", "mcp", "context_servers"];
    private static readonly string[] _runners = ["npx", "npx.cmd", "uvx", "bunx", "pnpx"];
    public const string NotPinned = "Not pinned: ", PlainKey = "Key in plain text";
    private const int MaxNames = 4;   // env names shown in a note

    /// <summary>Every server in the config files of a scan.</summary>
    public static List<McpServer> Read(IEnumerable<Item> items)
    {
        var res = new List<McpServer>();
        foreach (var g in items.Where(i => i.Kind == "Config" && i.Description is "MCP servers" or "Settings").GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
        {
            var first = g.First();
            var apps = g.Select(i => i.App).Distinct().ToList();
            foreach (var s in InFile(g.Key))
            {
                var local = s.Project != null && first.Project == null;   // kept per project in a user file
                res.Add(s with { Apps = apps, Level = local ? "Local" : first.Level, LevelLabel = local ? "Local (user file)" : first.LevelLabel, Project = s.Project ?? first.Project });
            }
        }

        return res;
    }

    /// <summary>The servers in one config file (JSON, JSONC, or Codex TOML); none when it cannot be read.</summary>
    public static List<McpServer> InFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var text = ext switch
        {
            ".toml" => Toml(ContextBudget.ReadText(path)),
            ".json" or ".jsonc" => ContextBudget.ReadText(path),
            _ => "",
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(text, Jsonc);
            return Parse(doc.RootElement, path);
        }
        catch (JsonException) { return []; }
    }

    internal static List<McpServer> Parse(JsonElement root, string path)
    {
        var res = new List<McpServer>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return res;
        }

        In(root, null);
        if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in projects.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
            {
                In(p.Value, p.Name.Replace('/', Path.DirectorySeparatorChar));
            }
        }

        return res;

        void In(JsonElement obj, string? project)
        {
            foreach (var key in _keys)
            {
                if (obj.TryGetProperty(key, out var servers) && servers.ValueKind == JsonValueKind.Object)
                {
                    res.AddRange(servers.EnumerateObject().Where(s => s.Value.ValueKind == JsonValueKind.Object)
                        .Select(s => One(s.Name, s.Value, path) is { } m ? m with { Project = project } : null).OfType<McpServer>());
                }
            }
        }
    }

    private static McpServer? One(string name, JsonElement v, string path)
    {
        v.TryGetProperty("command", out var c);
        List<string> parts = c.ValueKind switch
        {
            JsonValueKind.String => [c.GetString()!, .. Strings(v, "args")],
            JsonValueKind.Array => Strings(v, "command"),                                                     // OpenCode: "command": ["npx", "x"]
            JsonValueKind.Object when Str(c, "path") is { } exe => [exe, .. Strings(c, "args")],               // Zed: "command": { "path", "args", "env" }
            _ => [],
        };
        var url = Str(v, "url") ?? Str(v, "httpUrl") ?? Str(v, "serverUrl");
        if (parts.Count == 0 && url == null)
        {
            return null;
        }

        var notes = new List<string>();
        var warn = false;
        if (parts.Count > 0 && Unpinned(parts) is { } pkg)
        {
            notes.Add(NotPinned + pkg);
            warn = true;
        }

        var env = new[] { v, c }.Where(e => e.ValueKind == JsonValueKind.Object).SelectMany(e => new[] { "env", "environment", "headers" }
            .Select(k => e.TryGetProperty(k, out var o) && o.ValueKind == JsonValueKind.Object ? (Key: k, Value: o) : default).Where(o => o.Key != null)).ToList();
        if (KeyInArgs(parts) || (url != null && (TokenShape().IsMatch(url) || SecretAssign().IsMatch(url)))
            || env.Any(e => e.Value.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.String && LooksSecret(p.Name, p.Value.GetString()!))))
        {
            notes.Add(PlainKey);
            warn = true;
        }

        var names = env.Where(e => e.Key != "headers").SelectMany(e => e.Value.EnumerateObject().Select(p => p.Name)).Distinct().ToList();
        if (names.Count > 0)
        {
            notes.Add("env: " + string.Join(", ", names.Take(MaxNames)) + (names.Count > MaxNames ? $" +{names.Count - MaxNames} more" : ""));
        }

        if ((v.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True) || (v.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False))
        {
            notes.Add("disabled");
        }

        return new(name, parts.Count > 0 ? CommandLine(parts) : Mask(url!), path, notes.Count > 0 ? string.Join(" · ", notes) : null, warn) { Fingerprint = Services.Fingerprint.Text(v.GetRawText()) };
    }

    /// <summary>The package a runner (npx, uvx, bunx, pnpx, pnpm dlx) or docker/podman run starts without a fixed version, or null.</summary>
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

    [GeneratedRegex(@"^-(e|v|p|w|u|-env|-volume|-publish|-name|-network|-workdir|-user|-entrypoint)$")] private static partial Regex Flag();
}
