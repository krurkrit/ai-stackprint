// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiStackprint.ModelData;

/// <summary>Stable layout: two-space indent, one model per block, small objects (price, tiers, multipliers) on one line, LF line ends.</summary>
public static class Writer
{
    private static readonly JsonSerializerOptions _indented = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions _inline = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(JsonObject file)
    {
        var sb = new System.Text.StringBuilder("{\n");
        var top = file.ToList();
        for (var i = 0; i < top.Count; i++)
        {
            var (key, value) = top[i];
            sb.Append("  ").Append(Str(key)).Append(": ");
            if (key == "models" && value is JsonObject models)
            {
                Models(models, sb);
            }
            else
            {
                sb.Append(value!.ToJsonString(_indented).Replace("\n", "\n  ", StringComparison.Ordinal));
            }

            sb.Append(i < top.Count - 1 ? ",\n" : "\n");
        }
        return sb.Append("}\n").ToString();
    }

    private static void Models(JsonObject models, System.Text.StringBuilder sb)
    {
        sb.Append("{\n");
        var list = models.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var (name, m) = list[i];
            sb.Append("    ").Append(Str(name)).Append(": {\n");
            var fields = ((JsonObject)m!).ToList();
            for (var j = 0; j < fields.Count; j++)
            {
                sb.Append("      ").Append(Str(fields[j].Key)).Append(": ").Append(Inline(fields[j].Value));
                sb.Append(j < fields.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("    }").Append(i < list.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  }");
    }

    /// <summary>One line with a space after each colon and comma: <c>{ "input": 3, "output": 15 }</c>.</summary>
    private static string Inline(JsonNode? n) => n switch
    {
        JsonObject o => o.Count == 0 ? "{}" : "{ " + string.Join(", ", o.Select(p => Str(p.Key) + ": " + Inline(p.Value))) + " }",
        JsonArray a => a.Count == 0 ? "[]" : "[ " + string.Join(", ", a.Select(Inline)) + " ]",
        _ => n?.ToJsonString(_inline) ?? "null",
    };

    private static string Str(string s) => JsonValue.Create(s).ToJsonString(_inline);
}
