// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>
/// Views take sizes, spacing, radii and type sizes from the design tokens (src/App/Ui/Tokens.cs, docs/design-system.md), never from literals.
/// Zero is allowed. A named constant (<c>const double X = 240;</c>) is allowed for geometry that belongs to one component.
/// </summary>
public partial class DesignTokenTests
{
    private static readonly string[] _sizeCalls = ["Txt", "Sel", "Title", "Mono", "WithDot", "DotText", "Icon", "IconTile", "AppLogo", "AppName", "SearchBox", "Spinner", "Loading"];
    private static readonly string[] _exprCalls = ["new Thickness", "new CornerRadius", "Size", "new GridLength", "new Size"];

    [Fact]
    public void Views_use_design_tokens_not_literal_sizes()
    {
        var bad = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Root(), "src", "App", "Ui"), "*.cs"))
        {
            var name = Path.GetFileName(file);
            if (name is "Tokens.cs" or "Icons.cs")
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = Code(lines[i]);
                if (Const().IsMatch(code))
                {
                    continue;
                }

                foreach (var why in Problems(code, lines[i]))
                {
                    bad.Add($"{name}:{i + 1}: {why}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(bad.Count == 0, $"{bad.Count} literal sizes in views; use the tokens in Tokens.cs:\n" + string.Join("\n", bad));
    }

    private static IEnumerable<string> Problems(string code, string raw)
    {
        foreach (Match m in Assign().Matches(code))
        {
            yield return m.Groups[1].Value;
        }

        foreach (var call in _exprCalls)
        {
            foreach (var args in Calls(code, call))
            {
                if (args.Any(a => Number().Matches(a).Any(n => n.Value != "0")))
                {
                    yield return call;
                }
            }
        }

        foreach (var call in _sizeCalls)
        {
            foreach (var args in Calls(code, call))
            {
                if (args.Skip(1).Any(a => Number().IsMatch(a) && Number().Match(a).Value == a && a != "0"))
                {
                    yield return call;
                }
            }
        }

        // Grid sizes written as text: "170,*,48" (stars and Auto are proportions, not sizes)
        foreach (Match m in GridText().Matches(raw))
        {
            if (m.Groups[1].Value.Split(',').Any(p => double.TryParse(p.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var v) && v != 0))
            {
                yield return "grid definition";
            }
        }
    }

    /// <summary>Top-level arguments of each call to <paramref name="name"/> in <paramref name="code"/> (strings already removed).</summary>
    private static IEnumerable<List<string>> Calls(string code, string name)
    {
        foreach (Match m in Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(name)}\("))
        {
            var args = new List<string>();
            int depth = 1, start = m.Index + m.Length, i = start;
            for (; i < code.Length && depth > 0; i++)
            {
                switch (code[i])
                {
                    case '(' or '[' or '{': depth++; break;
                    case ')' or ']' or '}':
                        if (--depth == 0)
                        {
                            args.Add(code[start..i].Trim());
                        }
                        break;
                    case ',' when depth == 1: args.Add(code[start..i].Trim()); start = i + 1; break;
                }
            }

            yield return args;
        }
    }

    /// <summary>The line without string contents and comments.</summary>
    private static string Code(string line)
    {
        var s = Strings().Replace(line, "\"\"");
        var c = s.IndexOf("//", StringComparison.Ordinal);
        return c >= 0 ? s[..c] : s;
    }

    /// <summary>The repository root, found from this source file so it works from any build output folder.</summary>
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var d = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "AiStackprint.slnx")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new DirectoryNotFoundException("AiStackprint.slnx not found above the test folder");
    }

    [GeneratedRegex(@"\b((?:Min|Max)?(?:Width|Height)|(?:Item|Line|Column|Row)?Spacing|FontSize|StrokeThickness)\s*=\s*\d+(?:\.\d+)?\b(?<!=\s*0)")]
    private static partial Regex Assign();

    [GeneratedRegex(@"(?<![\w.])\d+(?:\.\d+)?(?![\w.])")]
    private static partial Regex Number();

    [GeneratedRegex(@"\bconst\s+(?:double|int|float)\s+\w+\s*=")]
    private static partial Regex Const();

    [GeneratedRegex("\\$?@?\"(?:[^\"\\\\]|\\\\.)*\"")]
    private static partial Regex Strings();

    [GeneratedRegex("(?:Column|Row)Definitions\\(\"([^\"]*)\"\\)")]
    private static partial Regex GridText();
}
