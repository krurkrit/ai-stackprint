// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>Finds the script a hook command runs ("bash scripts/x.sh", "node ./hook.js", "$CLAUDE_PROJECT_DIR/.claude/hooks/a.py") and tells whether it exists. Never runs anything.</summary>
public static partial class HookScript
{
    private static readonly HashSet<string> _runners = new(StringComparer.OrdinalIgnoreCase)
        { "bash", "sh", "zsh", "node", "python", "python3", "py", "uv", "run", "pwsh", "powershell", "cmd", "npx", "bun", "deno", "tsx", "ruby", "perl", "env", "exec" };
    private static readonly string[] _ext = [".sh", ".py", ".js", ".mjs", ".cjs", ".ts", ".ps1", ".cmd", ".bat", ".rb", ".pl"];

    /// <summary>The script path (as written, after the folder variables are filled in) when it does not exist; null when it exists or cannot be told.</summary>
    public static string? Missing(string command, string project, string home)
    {
        foreach (var token in Tokens(command))
        {
            if (token.StartsWith('-') || token.Equals("/c", StringComparison.OrdinalIgnoreCase) || _runners.Contains(token) || token.Contains('=') && !token.Contains('/') && !token.Contains('\\'))
            {
                continue;
            }

            var path = token.Replace("${CLAUDE_PROJECT_DIR}", project).Replace("$CLAUDE_PROJECT_DIR", project).Replace("%CLAUDE_PROJECT_DIR%", project);
            if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            {
                path = Path.Combine(home, path[2..]);
            }

            if (path.IndexOfAny(['$', '%', '*', '<', '>', '`', '|', '&', ';', '(', ')']) >= 0)
            {
                return null;   // a variable we cannot fill in, or shell syntax
            }

            var looksLikePath = path.Contains('/') || path.Contains('\\') || _ext.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
            if (!looksLikePath)
            {
                return null;   // a program on PATH (git, npx …): not ours to judge
            }

            try
            {
                var full = Path.IsPathRooted(path) ? path : Path.Combine(project, path);
                return File.Exists(full) || Directory.Exists(full) ? null : path;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        }

        return null;
    }

    private static IEnumerable<string> Tokens(string command) => Token().Matches(command).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);

    [GeneratedRegex("\"([^\"]*)\"|'([^']*)'|(\\S+)")] private static partial Regex Token();
}
