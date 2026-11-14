// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>Reading apps' config files for display: JSON with comments, and values that look secret, which are never shown.</summary>
internal static partial class ConfigJson
{
    public const string Hidden = "•••";
    public static readonly JsonDocumentOptions Jsonc = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>A literal secret: a known token shape, or a long value under a key/token/secret name that is not a reference (${VAR}, {env:VAR}, $VAR) or a placeholder.</summary>
    public static bool LooksSecret(string name, string value)
    {
        if (TokenShape().IsMatch(value))
        {
            return true;
        }

        var v = value.Trim();
        if (v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            v = v[7..].Trim();   // an Authorization header
        }

        return SecretName().IsMatch(name) && v.Length >= 16 && !v.Contains('$') && !v.Contains("{env:", StringComparison.OrdinalIgnoreCase)
            && !v.Contains(' ') && !v.StartsWith('<') && !Placeholder().IsMatch(v);
    }

    /// <summary>True when a command line holds a key: a token-shaped argument, or a value after a --token / --api-key style flag.</summary>
    public static bool KeyInArgs(IReadOnlyList<string> parts) =>
        parts.Select((p, i) => TokenShape().IsMatch(p) || (i > 0 && SecretFlag().IsMatch(parts[i - 1]) && !p.Contains('$'))).Any(b => b);

    /// <summary>A command line to show: arguments that look secret, or follow a --token / --api-key style flag, are hidden.</summary>
    public static string CommandLine(IReadOnlyList<string> parts) =>
        string.Join(" ", parts.Select((p, i) => i > 0 && (SecretFlag().IsMatch(parts[i - 1]) || TokenShape().IsMatch(p)) ? Hidden : Mask(p)));

    /// <summary>Text with token-shaped parts, key=value secrets and long random-looking runs (keys in an address's path) hidden.</summary>
    public static string Mask(string s) => LongToken().Replace(SecretAssign().Replace(TokenShape().Replace(s, Hidden), m => m.Groups[1].Value + Hidden), Hidden);

    public static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static List<string> Strings(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
        ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)] : [];

    [GeneratedRegex(@"\b(?:sk-(?:ant-|proj-)?[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|xox[abprs]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35}|glpat-[A-Za-z0-9_-]{20,}|npm_[A-Za-z0-9]{36}|hf_[A-Za-z0-9]{30,})")]
    public static partial Regex TokenShape();
    [GeneratedRegex(@"(?i)(api[_-]?key|apikey|token|secret|password|passwd|auth|credential|private[_-]?key)")] private static partial Regex SecretName();
    [GeneratedRegex(@"(?i)^(x{3,}|\.{3,}|\*{3,}|your[_-]|changeme|placeholder|example|<)")] private static partial Regex Placeholder();
    [GeneratedRegex(@"(?i)^--?(api[_-]?key|token|secret|password|auth)")] private static partial Regex SecretFlag();
    [GeneratedRegex(@"(?i)((?:api[_-]?key|token|secret|password)=)[^\s&""']+")] public static partial Regex SecretAssign();
    [GeneratedRegex(@"(?<![A-Za-z0-9_-])(?=[A-Za-z0-9_-]*\d)(?=[A-Za-z0-9_-]*[A-Za-z])[A-Za-z0-9_-]{32,}")] private static partial Regex LongToken();
}
