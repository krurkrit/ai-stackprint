// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>
/// One step of a session. Role: user | assistant | tool | result | thinking | system.
/// Lang: file extension for syntax highlighting (".sh", ".json", ".cs"…); Detail: a tool call's full input.
/// </summary>
public record LogEntry(string Role, string Text, DateTime? Time, bool SubAgent, string? Tool = null, string? Lang = null, string? Detail = null, string? Agent = null);

public record Session(List<LogEntry> Entries, HashSet<string> Models, long InputTokens, long OutputTokens, DateTime? Start, DateTime? End);

/// <summary>Turns a session log (JSON lines) into readable steps. To support a new format: add a class and list it in LogParsers.All.</summary>
public interface ILogParser
{
    string Format { get; }
    Session Parse(IEnumerable<string> lines);

    /// <summary>Billed responses in the session (for the Usage card). Formats without usage data return nothing.</summary>
    IEnumerable<UsageEvent> Usage(IEnumerable<string> lines) => Array.Empty<UsageEvent>();

    /// <summary>Usage of one file. Line formats read its newest <see cref="LogSummary.SessionBytes"/>; a database reads it whole.</summary>
    IEnumerable<UsageEvent> UsageFile(string path) => Usage(LogReader.Lines(path, LogSummary.SessionBytes));

    /// <summary>True when <see cref="UsageFile"/> reads the whole file itself (a database): no size cap, no limit readings.</summary>
    bool WholeFile => false;

    /// <summary>Usage-limit readings the app wrote into the log (used % per window, reset time). Formats without them return nothing.</summary>
    IEnumerable<LimitSample> Limits(IEnumerable<string> lines) => Array.Empty<LimitSample>();
}

public static class LogParsers
{
    private static readonly ILogParser[] _all = [new ClaudeSessionParser(), new CodexSessionParser(), new OpenCodeUsage()];
    public static ILogParser? Find(string? format) => format == null ? null : _all.FirstOrDefault(p => p.Format == format);

    /// <summary>Each line as JSON; lines that are not JSON (or cut off) are skipped.</summary>
    internal static IEnumerable<JsonElement> Json(IEnumerable<string> lines)
    {
        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l))
            {
                continue;
            }

            JsonElement e;
            try { using var d = JsonDocument.Parse(l); e = d.RootElement.Clone(); }
            catch (JsonException) { continue; }
            yield return e;
        }
    }

    internal static string Str(this JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    internal static JsonElement Get(this JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) ? v : default;
    internal static long Num(this JsonElement e, string k) => e.Get(k) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : 0;
    internal static DateTime? Time(this JsonElement e) => DateTime.TryParse(e.Str("timestamp"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime() : null;

    /// <summary>Text of a string or of a list of {type:text|input_text|output_text, text} blocks.</summary>
    internal static string Text(JsonElement content) => content.ValueKind switch
    {
        JsonValueKind.String => content.GetString()!,
        JsonValueKind.Array => string.Join("\n", content.EnumerateArray().Select(b => b.ValueKind == JsonValueKind.String ? b.GetString()! : b.Str("text")).Where(t => t != "")),
        _ => ""
    };

    /// <summary>The most telling argument of a tool call (command, file, pattern, task), else compact JSON.</summary>
    internal static string Summary(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.String)
        {
            try { using var d = JsonDocument.Parse(input.GetString()!); return Summary(d.RootElement.Clone()); }
            catch (JsonException) { return Cut(input.GetString()!, 300); }
        }
        foreach (var k in new[] { "command", "cmd", "file_path", "path", "pattern", "url", "query", "description", "prompt" })
        {
            if (input.Get(k) is { ValueKind: not JsonValueKind.Undefined } v)
            {
                return Cut(v.ValueKind == JsonValueKind.Array ? string.Join(" ", v.EnumerateArray().Select(x => x.ToString())) : v.ToString(), 300);
            }
        }

        return Cut(input.ValueKind == JsonValueKind.Undefined ? "" : input.GetRawText(), 300);
    }

    /// <summary>Last folder of a working directory (either slash style), for grouping usage by project.</summary>
    internal static string ProjectName(string cwd)
    {
        var t = cwd.TrimEnd('/', '\\');
        return t == "" ? "(unknown)" : t[(t.LastIndexOfAny(['/', '\\']) + 1)..];
    }

    internal static string Cut(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static readonly string[] _shells = ["bash", "shell", "local_shell", "exec_command", "run_command", "powershell"];

    /// <summary>A tool call step with its full input and a highlighting hint (shell for commands, JSON otherwise).</summary>
    internal static LogEntry ToolCall(string name, JsonElement input, DateTime? time, bool sub)
    {
        if (input.ValueKind == JsonValueKind.String)
        {
            try { using var d = JsonDocument.Parse(input.GetString()!); input = d.RootElement.Clone(); }
            catch (JsonException) { return new("tool", Cut(input.GetString()!, 300), time, sub, name, ".txt", input.GetString()); }
        }
        var shell = _shells.Contains(name.ToLowerInvariant());
        var cmd = input.Get("command");
        var detail = shell && cmd.ValueKind != JsonValueKind.Undefined
            ? (cmd.ValueKind == JsonValueKind.Array ? string.Join(" ", cmd.EnumerateArray().Select(x => x.ToString())) : cmd.ToString())
            : input.ValueKind == JsonValueKind.Undefined ? "" : JsonSerializer.Serialize(input, _pretty);
        return new("tool", Summary(input), time, sub, name, shell ? ".sh" : ".json", detail);
    }

    private static readonly JsonSerializerOptions _pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Highlighting hint for tool output: the file type a Read returned, else JSON or diff when the text looks like it.</summary>
    internal static string? Detect(string text, string? fileHint)
    {
        if (fileHint != null)
        {
            return fileHint;
        }

        var t = text.TrimStart();
        if (t.StartsWith("diff --git") || t.StartsWith("--- ") || t.StartsWith("@@ ") || t.StartsWith("*** Begin Patch"))
        {
            return ".diff";
        }

        if (t.StartsWith('{') || t.StartsWith('['))
        {
            try { using var _ = JsonDocument.Parse(t); return ".json"; } catch (JsonException) { }
        }

        return null;
    }

    /// <summary>File extension of a tool input's file path (Read/Edit/Write), used to highlight what the tool returned.</summary>
    internal static string? FileHint(JsonElement input) =>
        (input.Str("file_path") is { Length: > 0 } f ? f : input.Str("path")) is { Length: > 0 } p && Path.HasExtension(p) ? Path.GetExtension(p) : null;

    internal static Session Build(List<LogEntry> entries, HashSet<string> models, long input, long output) =>
        new(entries, models, input, output, entries.FirstOrDefault(e => e.Time != null)?.Time, entries.LastOrDefault(e => e.Time != null)?.Time);
}
