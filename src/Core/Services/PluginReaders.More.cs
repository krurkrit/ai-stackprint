// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Codex: ~/.codex/config.toml lists plugins as [plugins."name@marketplace"] with enabled = true | false; each is kept in
/// ~/.codex/plugins/cache/marketplace/name/version/ with a .codex-plugin/plugin.json. The newest version folder is read.
/// </summary>
internal sealed partial class CodexPluginReader : IPluginReader
{
    private static readonly string[] _manifests = [".codex-plugin/plugin.json", ".claude-plugin/plugin.json"];

    public string Format => "codex";

    public void Read(string path, PluginContext ctx, List<Item> rows)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var cache = Path.Combine(Path.GetDirectoryName(path)!, "plugins", "cache");
        try
        {
            string? id = null;
            var enabled = true;
            void Flush()
            {
                if (id == null)
                {
                    return;
                }

                var at = id.IndexOf('@');
                var (name, market) = at > 0 ? (id[..at], id[(at + 1)..]) : (id, "");
                var dir = Newest(Path.Combine(cache, market, name));
                var bundled = market.StartsWith("openai-", StringComparison.Ordinal);   // ships with Codex
                if (dir != null)
                {
                    PluginManifests.Add(rows, ctx, dir, id, _manifests, enabled ? null : "disabled", bundled, Path.GetFileName(dir));
                }
                else
                {
                    rows.Add(new Item("Plugin", id, "", ctx.App.Name, ctx.Level, path) { App = ctx.App.Id, Scope = enabled ? null : "disabled", Bundled = bundled });
                }
            }

            foreach (var line in File.ReadLines(path))
            {
                var t = line.Trim();
                if (t.StartsWith('['))
                {
                    Flush();
                    id = Section().Match(t) is { Success: true } m ? m.Groups[1].Value : null;
                    enabled = true;
                }
                else if (id != null && Enabled().Match(t) is { Success: true } e)
                {
                    enabled = e.Groups[1].Value == "true";
                }
            }

            Flush();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The highest version folder that holds a manifest (folders that are not versions count by date).</summary>
    private static string? Newest(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateDirectories(folder).Where(d => PluginManifests.Find(d, _manifests) != null)
            .OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version())
            .ThenByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
    }

    [GeneratedRegex("""^\[plugins\."([^"]+)"\]$""")]
    private static partial Regex Section();

    [GeneratedRegex(@"^enabled\s*=\s*(true|false)\b")]
    private static partial Regex Enabled();
}

/// <summary>
/// A folder of plugin or extension folders, each with a manifest (<see cref="PluginManifests"/>): Gemini and Qwen extensions,
/// Copilot CLI and Cursor plugins, Claude Desktop extensions, OpenClaw, Hermes and Zed. Plugin folders may sit up to
/// <see cref="PluginSource.Depth"/> levels down (marketplace/name); of several copies of one plugin the highest version, then the newest, is listed.
/// </summary>
internal sealed class FolderPluginReader : IPluginReader
{
    public string Format => "folders";

    public void Read(string path, PluginContext ctx, List<Item> rows)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var manifests = ctx.Source?.Manifests is { Count: > 0 } m ? m : PluginManifests.Known;
        var found = new List<string>();
        void Walk(string dir, int depth)
        {
            foreach (var d in Directory.EnumerateDirectories(dir).Where(d => !Path.GetFileName(d).StartsWith('.')))
            {
                if (PluginManifests.Find(d, manifests) != null)
                {
                    found.Add(d);
                }
                else if (depth > 1)
                {
                    Walk(d, depth - 1);
                }
            }
        }

        try
        {
            Walk(path, Math.Max(1, ctx.Source?.Depth ?? 1));
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var read = found.Select(d => (Dir: d, Manifest: PluginManifests.Read(d, manifests)))
                .OrderByDescending(p => Version.TryParse(p.Manifest.Version, out var v) ? v : new Version()).ThenByDescending(p => Directory.GetLastWriteTimeUtc(p.Dir));
            foreach (var (d, m2) in read)
            {
                if (added.Add(m2.Name ?? Path.GetFileName(d)))   // an older cached copy of the same plugin: skipped
                {
                    PluginManifests.Add(rows, ctx, d, manifests: manifests, read: m2);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Editor extensions in VS Code's layout (Cursor, Windsurf, Kiro, Antigravity, Trae, Qoder): ~/.app/extensions/publisher.name-version
/// with a package.json. Folders listed in .obsolete are being removed; of several versions the highest is listed.
/// </summary>
internal sealed class VsCodeExtensionReader : IPluginReader
{
    private static readonly string[] _manifest = ["package.json"];

    public string Format => "vscode";

    public void Read(string path, PluginContext ctx, List<Item> rows)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            var obsolete = Obsolete(Path.Combine(path, ".obsolete"));
            var dirs = Directory.EnumerateDirectories(path)
                .Where(d => !Path.GetFileName(d).StartsWith('.') && !obsolete.Contains(Path.GetFileName(d)) && File.Exists(Path.Combine(d, "package.json")))
                .Select(d => (Dir: d, Id: Id(Path.GetFileName(d)), Version: VersionOf(Path.GetFileName(d))));
            foreach (var g in dirs.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
            {
                PluginManifests.Add(rows, ctx, g.OrderByDescending(d => d.Version).First().Dir, manifests: _manifest);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    /// <summary>publisher.name-1.2.3[-platform] → publisher.name.</summary>
    private static string Id(string folder) => Regex.Replace(folder, @"-\d+(\.\d+)*.*$", "");

    private static Version VersionOf(string folder) => Regex.Match(folder, @"-(\d+(?:\.\d+){1,3})") is { Success: true } m && Version.TryParse(m.Groups[1].Value, out var v) ? v : new Version();

    private static HashSet<string> Obsolete(string file)
    {
        if (!File.Exists(file))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.ValueKind == JsonValueKind.Object ? [.. doc.RootElement.EnumerateObject().Select(p => p.Name)] : [];
    }
}
