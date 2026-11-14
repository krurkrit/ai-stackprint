// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Checks of the user-level and system-level config files the apps read in every project (settings and MCP files in your home folder or managed locations): invalid JSON,
/// secrets stored in plain text, permission settings that skip prompts, hooks that run missing scripts. Only JSON files; TOML and YAML files are not checked.
/// Files an app rewrites on its own (<see cref="Item.AppWritten"/>) are skipped. Values that look secret are never shown.
/// </summary>
public static class ConfigChecks
{
    public static List<HealthIssue> ForHealth(IEnumerable<Item> items, string home)
    {
        var res = new List<HealthIssue>();
        foreach (var i in items.Where(i => i.Kind == "Config" && !Scanner.InProject(i.Level) && !i.AppWritten && i.Description is "Settings" or "MCP servers"
            && Path.GetExtension(i.Path).StartsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(i.Path)))
        {
            var issues = new List<ProjectIssue>();
            var rel = ContextBudget.Inside(i.Path, home) ? "~" + Path.DirectorySeparatorChar + Path.GetRelativePath(home, i.Path) : i.Path;
            try
            {
                using var doc = JsonDocument.Parse(ContextBudget.ReadText(i.Path), ConfigJson.Jsonc);
                var apps = new List<string> { i.App };
                ProjectChecks.Secrets(doc.RootElement, "", rel, i.Path, apps, true, issues);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    ProjectChecks.Hooks(doc.RootElement, home, i.Path, apps, [], issues);
                    ProjectChecks.Permissions(doc.RootElement, rel, i.Path, apps, issues);
                }
            }
            catch (JsonException e) { issues.Add(new([i.App], i.Path, new($"{rel} is not valid JSON (line {e.LineNumber + 1}): the app may ignore it."))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

            res.AddRange(issues.Select(x => new HealthIssue("Config files", i.Name, i.App, x.Finding, i)));
        }

        return res;
    }
}
