// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// Makes log text readable: decodes JSON escapes (\u001b, \n), turns ANSI shell colors into colored runs (or strips them),
/// pretty-prints JSON found inside text and shortens inline image data. Pure functions; the UI decides how to show the result.
/// </summary>
public static partial class TextDecode
{
    /// <param name="Color">0-15 = ANSI palette index; 0x1000000 | RGB = 24-bit color; null = the normal text color.</param>
    public record AnsiRun(string Text, int? Color, bool Bold);

    /// <summary>The text ready to show. Ansi is set when the text has shell colors (then Text is the same text without the codes). Changed = differs from the original.</summary>
    public record Prepared(string Text, string? Lang, List<AnsiRun>? Ansi, bool Changed);

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|\x1B[@-Z\\-_]")]
    private static partial Regex AnsiCode();
    [GeneratedRegex(@"\\u[0-9a-fA-F]{4}|\\[nrt""\\/]")]
    private static partial Regex JsonEscape();
    [GeneratedRegex(@"data:image/[a-z+.-]+;base64,[A-Za-z0-9+/=]{200,}")]
    private static partial Regex DataUrl();

    private static readonly JsonSerializerOptions _pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Removes ANSI escape sequences (colors, cursor moves, window titles).</summary>
    public static string Strip(string s) => s.Contains('\x1B') ? AnsiCode().Replace(s, "").Replace("\r\n", "\n") : s;

    /// <summary>True when the text holds JSON escapes as characters (\u001b, \n) instead of real ones: mostly one line with escapes in it.</summary>
    public static bool LooksEscaped(string s)
    {
        var escapes = JsonEscape().Count(s);
        if (escapes == 0)
        {
            return false;
        }

        var lines = 0;
        foreach (var c in s)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return lines == 0 || escapes > lines * 2;   // code that merely contains "\n" in a few places stays as it is
    }

    /// <summary>Decodes \uXXXX, \n, \r, \t, \", \\ and \/ in one pass (so \\n stays a backslash and an n).</summary>
    public static string Unescape(string s) => JsonEscape().Replace(s, m => m.Value[1] switch
    {
        'u' => ((char)Convert.ToInt32(m.Value[2..], 16)).ToString(),
        'n' => "\n",
        'r' => "\r",
        't' => "\t",
        '"' => "\"",
        '\\' => "\\",
        '/' => "/",
        _ => m.Value
    });

    /// <summary>Short one-line-safe form for headers: escapes decoded, colors removed.</summary>
    public static string Clean(string s) => Strip(LooksEscaped(s) ? Unescape(s) : s);

    /// <summary>The ANSI text as colored runs (SGR codes: 30-37, 90-97, 38;5;n, 38;2;r;g;b, bold, reset). Other codes are dropped.</summary>
    public static List<AnsiRun> Runs(string s)
    {
        var runs = new List<AnsiRun>();
        var at = 0;
        int? fg = null;
        var bold = false;
        foreach (Match m in AnsiCode().Matches(s))
        {
            if (m.Index > at)
            {
                runs.Add(new(s[at..m.Index], fg, bold));
            }

            at = m.Index + m.Length;
            var v = m.Value;
            if (v.Length < 3 || v[1] != '[' || v[^1] != 'm')
            {
                continue;
            }

            var codes = v[2..^1].Split(';');
            for (var i = 0; i < codes.Length; i++)
            {
                var c = codes[i] == "" ? 0 : int.TryParse(codes[i], out var n) ? n : -1;
                if (c == 0)
                {
                    fg = null;
                    bold = false;
                }
                else if (c == 1)
                {
                    bold = true;
                }
                else if (c == 22)
                {
                    bold = false;
                }
                else if (c is >= 30 and <= 37)
                {
                    fg = c - 30;
                }
                else if (c is >= 90 and <= 97)
                {
                    fg = c - 90 + 8;
                }
                else if (c == 39)
                {
                    fg = null;
                }
                else if (c == 38 && i + 1 < codes.Length)
                {
                    if (codes[i + 1] == "5" && i + 2 < codes.Length && int.TryParse(codes[i + 2], out var idx))
                    {
                        fg = Palette256(idx);
                        i += 2;
                    }
                    else if (codes[i + 1] == "2" && i + 4 < codes.Length && int.TryParse(codes[i + 2], out var r) && int.TryParse(codes[i + 3], out var g) && int.TryParse(codes[i + 4], out var b))
                    {
                        fg = Rgb(r, g, b);
                        i += 4;
                    }
                }
            }
        }
        if (at < s.Length)
        {
            runs.Add(new(s[at..], fg, bold));
        }

        return [.. runs.Where(r => r.Text.Length > 0).Select(r => r with { Text = r.Text.Replace("\r\n", "\n") })];
    }

    private static int Rgb(int r, int g, int b) => 0x1000000 | (Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255);

    private static int Palette256(int i)
    {
        if (i < 0 || i > 255)
        {
            return 7;
        }

        if (i < 16)
        {
            return i;
        }

        if (i >= 232)
        {
            var g = 8 + 10 * (i - 232);
            return Rgb(g, g, g);
        }
        var v = i - 16;
        return Rgb(Level(v / 36), Level(v / 6 % 6), Level(v % 6));
    }

    private static int Level(int x) => x == 0 ? 0 : 55 + 40 * x;

    private static bool JsonLike(string? lang) => lang is null or ".txt" or ".json";

    /// <summary>Pretty JSON when the text is a JSON object/array, or a JSON string that contains one. False when it is not (or was cut off).</summary>
    public static bool TryPrettyJson(string text, out string pretty)
    {
        pretty = "";
        var t = text.Trim();
        if (t.Length < 2 || t.Length > 200_000)
        {
            return false;
        }

        try
        {
            if (t[0] == '"' && t[^1] == '"')
            {
                t = JsonSerializer.Deserialize<string>(t)?.Trim() ?? "";
            }

            if (t.Length < 2 || !((t[0] == '{' && t[^1] == '}') || (t[0] == '[' && t[^1] == ']')))
            {
                return false;
            }

            using var d = JsonDocument.Parse(t);
            pretty = JsonSerializer.Serialize(d.RootElement, _pretty).Replace("\r\n", "\n");
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Text of a result, a reply or a thinking step, ready to show: JSON (also inside a JSON string) pretty-printed, escapes decoded,
    /// shell colors parsed, image data shortened. <paramref name="lang"/> is the highlighting hint from the parser.
    /// </summary>
    public static Prepared Prepare(string text, string? lang)
    {
        if (JsonLike(lang) && !text.Contains('\x1B') && TryPrettyJson(text, out var p0))
        {
            return new(p0, ".json", null, p0 != text);
        }

        var t = text;
        var changed = false;
        if (LooksEscaped(t))
        {
            t = Unescape(t);
            changed = true;
        }
        var shortened = DataUrl().Replace(t, m => $"[image data, {Math.Max(1, m.Length * 3 / 4 / 1024)} KB]");
        if (shortened != t)
        {
            t = shortened;
            changed = true;
        }
        if (t.Contains('\x1B'))
        {
            return new(Strip(t), null, Runs(t), true);
        }

        if (JsonLike(lang) && TryPrettyJson(t, out var p1))
        {
            return new(p1, ".json", null, true);
        }

        return new(t, lang, null, changed);
    }
}
