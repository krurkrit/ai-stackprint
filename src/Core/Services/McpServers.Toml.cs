// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace AiStackprint.Services;

public static partial class McpServers
{
    /// <summary>
    /// Codex config.toml: the [mcp_servers.*] tables as {"mcpServers": {…}} JSON, so they are read like every other app's servers.
    /// Only what servers use is read: strings, arrays (also over several lines), inline tables, booleans; other tables are skipped.
    /// </summary>
    internal static string Toml(string text)
    {
        var servers = new JsonObject();
        JsonObject? table = null;   // where key = value lines go; null outside mcp_servers
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                var keys = line.StartsWith("[[", StringComparison.Ordinal) ? [] : Keys(line[1..Math.Max(1, line.LastIndexOf(']'))]);
                table = keys.Count > 0 && keys[0] == "mcp_servers" ? Table(servers, keys.Skip(1)) : null;
                continue;
            }

            var eq = Outside(line, '=');
            if (table == null || eq < 0)
            {
                continue;
            }

            var rest = new StringBuilder(line[(eq + 1)..].Trim());
            while (Depth(rest.ToString()) > 0 && n + 1 < lines.Length)
            {
                rest.Append('\n').Append(lines[++n].Trim());   // an array or table over several lines
            }

            var i = 0;
            var key = Keys(line[..eq]);
            if (Value(rest.ToString(), ref i) is { } value && key.Count > 0 && Table(table, key.SkipLast(1)) is { } at)
            {
                at[key[^1]] = value;
            }
        }

        return new JsonObject { ["mcpServers"] = servers }.ToJsonString();
    }

    /// <summary>The table at <paramref name="keys"/> under <paramref name="root"/>, created as needed; null when a value is in the way.</summary>
    private static JsonObject? Table(JsonObject root, IEnumerable<string> keys)
    {
        var at = root;
        foreach (var k in keys)
        {
            if (at[k] is null)
            {
                at[k] = new JsonObject();
            }

            if (at[k] is not JsonObject next)
            {
                return null;
            }

            at = next;
        }

        return at;
    }

    /// <summary>Dotted keys: a.b."c.d" → [a, b, c.d].</summary>
    private static List<string> Keys(string s)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        char? quote = null;
        foreach (var c in s)
        {
            if (quote != null) { if (c == quote) { quote = null; } else { sb.Append(c); } }
            else if (c is '"' or '\'') { quote = c; }
            else if (c == '.')
            {
                res.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else { sb.Append(c); }
        }

        res.Add(sb.ToString().Trim());
        return res.Any(k => k.Length == 0) ? [] : res;
    }

    private static JsonNode? Value(string s, ref int i)
    {
        Skip(s, ref i);
        if (i >= s.Length)
        {
            return null;
        }

        switch (s[i])
        {
            case '"' or '\'':
                return JsonValue.Create(Quoted(s, ref i));
            case '[':
                var arr = new JsonArray();
                i++;
                while (true)
                {
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] == ']')
                    {
                        i++;
                        return arr;
                    }
                    if (Value(s, ref i) is { } v) { arr.Add(v); }
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != ',')
                    {
                        i++;
                        return arr;
                    }   // ']' (or the end)
                    i++;
                }

            case '{':
                var obj = new JsonObject();
                i++;
                while (true)
                {
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] == '}')
                    {
                        i++;
                        return obj;
                    }
                    var eq = Outside(s[i..], '=');
                    if (eq < 0) { return obj; }
                    var key = Keys(s[i..(i + eq)]);
                    i += eq + 1;
                    if (Value(s, ref i) is { } v && key.Count > 0 && Table(obj, key.SkipLast(1)) is { } at) { at[key[^1]] = v; }
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != ',')
                    {
                        i++;
                        return obj;
                    }   // '}' (or the end)
                    i++;
                }

            default:
                var start = i;
                while (i < s.Length && s[i] is not (',' or ']' or '}' or '#') && !char.IsWhiteSpace(s[i]))
                {
                    i++;
                }

                var word = s[start..i];
                return word switch
                {
                    "true" => JsonValue.Create(true),
                    "false" => JsonValue.Create(false),
                    _ when double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
                    _ => JsonValue.Create(word),
                };
        }
    }

    /// <summary>A "basic" (escapes) or 'literal' string; i ends after the closing quote.</summary>
    private static string Quoted(string s, ref int i)
    {
        var q = s[i++];
        var sb = new StringBuilder();
        for (; i < s.Length && s[i] != q; i++)
        {
            if (q == '"' && s[i] == '\\' && i + 1 < s.Length)
            {
                var e = s[++i];
                sb.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => e });
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        i++;
        return sb.ToString();
    }

    /// <summary>Skips spaces, new lines and # comments.</summary>
    private static void Skip(string s, ref int i)
    {
        while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == '#'))
        {
            if (s[i] == '#')
            {
                while (i < s.Length && s[i] != '\n') { i++; }
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>The first <paramref name="c"/> outside quotes, or -1.</summary>
    private static int Outside(string s, char c)
    {
        char? quote = null;
        for (var i = 0; i < s.Length; i++)
        {
            if (quote != null) { if (s[i] == quote) { quote = null; } }
            else if (s[i] is '"' or '\'') { quote = s[i]; }
            else if (s[i] == c) { return i; }
        }

        return -1;
    }

    /// <summary>Open [ and { outside quotes and comments: more than 0 means the value goes on.</summary>
    private static int Depth(string s)
    {
        var depth = 0;
        char? quote = null;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quote != null) { if (c == quote) { quote = null; } else if (c == '\\' && quote == '"') { i++; } }
            else if (c is '"' or '\'') { quote = c; }
            else if (c == '#') { while (i < s.Length && s[i] != '\n') { i++; } }
            else if (c is '[' or '{') { depth++; }
            else if (c is ']' or '}') { depth--; }
        }

        return depth;
    }
}
