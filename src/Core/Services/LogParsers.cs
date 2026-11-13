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
    static readonly ILogParser[] All = { new ClaudeSessionParser(), new CodexSessionParser(), new OpenCodeUsage() };
    public static ILogParser? Find(string? format) => format == null ? null : All.FirstOrDefault(p => p.Format == format);

    /// <summary>Each line as JSON; lines that are not JSON (or cut off) are skipped.</summary>
    internal static IEnumerable<JsonElement> Json(IEnumerable<string> lines)
    {
        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l)) continue;
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
            if (input.Get(k) is { ValueKind: not JsonValueKind.Undefined } v)
                return Cut(v.ValueKind == JsonValueKind.Array ? string.Join(" ", v.EnumerateArray().Select(x => x.ToString())) : v.ToString(), 300);
        return Cut(input.ValueKind == JsonValueKind.Undefined ? "" : input.GetRawText(), 300);
    }

    /// <summary>Last folder of a working directory (either slash style), for grouping usage by project.</summary>
    internal static string ProjectName(string cwd)
    {
        var t = cwd.TrimEnd('/', '\\');
        return t == "" ? "(unknown)" : t[(t.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];
    }

    internal static string Cut(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    static readonly string[] Shells = { "bash", "shell", "local_shell", "exec_command", "run_command", "powershell" };

    /// <summary>A tool call step with its full input and a highlighting hint (shell for commands, JSON otherwise).</summary>
    internal static LogEntry ToolCall(string name, JsonElement input, DateTime? time, bool sub)
    {
        if (input.ValueKind == JsonValueKind.String)
        {
            try { using var d = JsonDocument.Parse(input.GetString()!); input = d.RootElement.Clone(); }
            catch (JsonException) { return new("tool", Cut(input.GetString()!, 300), time, sub, name, ".txt", input.GetString()); }
        }
        bool shell = Shells.Contains(name.ToLowerInvariant());
        var cmd = input.Get("command");
        string detail = shell && cmd.ValueKind != JsonValueKind.Undefined
            ? (cmd.ValueKind == JsonValueKind.Array ? string.Join(" ", cmd.EnumerateArray().Select(x => x.ToString())) : cmd.ToString())
            : input.ValueKind == JsonValueKind.Undefined ? "" : JsonSerializer.Serialize(input, Pretty);
        return new("tool", Summary(input), time, sub, name, shell ? ".sh" : ".json", detail);
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Highlighting hint for tool output: the file type a Read returned, else JSON or diff when the text looks like it.</summary>
    internal static string? Detect(string text, string? fileHint)
    {
        if (fileHint != null) return fileHint;
        var t = text.TrimStart();
        if (t.StartsWith("diff --git") || t.StartsWith("--- ") || t.StartsWith("@@ ") || t.StartsWith("*** Begin Patch")) return ".diff";
        if (t.StartsWith('{') || t.StartsWith('['))
            try { using var _ = JsonDocument.Parse(t); return ".json"; } catch (JsonException) { }
        return null;
    }

    /// <summary>File extension of a tool input's file path (Read/Edit/Write), used to highlight what the tool returned.</summary>
    internal static string? FileHint(JsonElement input) =>
        (input.Str("file_path") is { Length: > 0 } f ? f : input.Str("path")) is { Length: > 0 } p && Path.HasExtension(p) ? Path.GetExtension(p) : null;

    internal static Session Build(List<LogEntry> entries, HashSet<string> models, long input, long output) =>
        new(entries, models, input, output, entries.FirstOrDefault(e => e.Time != null)?.Time, entries.LastOrDefault(e => e.Time != null)?.Time);
}

/// <summary>Claude Code: ~/.claude/projects/&lt;project&gt;/&lt;session&gt;.jsonl (sub-agent steps have isSidechain: true).</summary>
class ClaudeSessionParser : ILogParser
{
    public string Format => "claude-session";

    public Session Parse(IEnumerable<string> lines)
    {
        var list = new List<LogEntry>(); var models = new HashSet<string>(); long inTok = 0, outTok = 0;
        var seen = new HashSet<string>();   // a streamed reply is written on several lines with the same id and usage: count it once
        var calls = new Dictionary<string, (string Tool, string? File)>();   // tool_use id -> name, file type it touched
        int start = 0; string agent = "";
        void Tag() { if (agent != "") for (int k = start; k < list.Count; k++) list[k] = list[k] with { Agent = agent }; start = list.Count; }   // sub-agent id on the steps of the previous line
        foreach (var e in LogParsers.Json(lines))
        {
            Tag(); agent = e.Str("agentId");
            var type = e.Str("type"); var time = e.Time();
            bool sub = e.Get("isSidechain").ValueKind == JsonValueKind.True;
            if (type == "summary") { list.Add(new("system", "Summary: " + e.Str("summary"), time, sub)); continue; }
            if (type is not ("user" or "assistant")) continue;
            var msg = e.Get("message");
            if (msg.Str("model") is { Length: > 0 } m && m != "<synthetic>") models.Add(m);
            if (msg.Str("id") is not { Length: > 0 } mid || seen.Add(mid + ":" + e.Str("requestId")))
            { inTok += msg.Get("usage").Num("input_tokens"); outTok += msg.Get("usage").Num("output_tokens"); }
            var content = msg.Get("content");
            if (content.ValueKind == JsonValueKind.String) { list.Add(new(type, content.GetString()!, time, sub)); continue; }
            if (content.ValueKind != JsonValueKind.Array) continue;
            foreach (var b in content.EnumerateArray())
                switch (b.Str("type"))
                {
                    case "text" when b.Str("text").Trim() != "": list.Add(new(type, b.Str("text"), time, sub)); break;
                    case "thinking": list.Add(new("thinking", b.Str("thinking"), time, sub)); break;
                    case "tool_use":
                        calls[b.Str("id")] = (b.Str("name"), LogParsers.FileHint(b.Get("input")));
                        list.Add(LogParsers.ToolCall(b.Str("name"), b.Get("input"), time, sub));
                        break;
                    case "tool_result":
                        var text = LogParsers.Cut(LogParsers.Text(b.Get("content")), 20000);
                        calls.TryGetValue(b.Str("tool_use_id"), out var call);
                        list.Add(new("result", text, time, sub, call.Tool, LogParsers.Detect(text, call.Tool == "Read" ? call.File : null)));
                        break;
                }
        }
        Tag();
        return LogParsers.Build(list, models, inTok, outTok);
    }

    public IEnumerable<UsageEvent> Usage(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>();
        foreach (var e in LogParsers.Json(lines))
        {
            if (e.Str("type") != "assistant") continue;
            var msg = e.Get("message"); var u = msg.Get("usage");
            if (u.ValueKind != JsonValueKind.Object || e.Time() is not { } time) continue;
            if (msg.Str("id") is { Length: > 0 } id && !seen.Add(id + ":" + e.Str("requestId"))) continue;   // streamed reply: count once
            var model = msg.Str("model");
            if (model is "" or "<synthetic>") continue;
            yield return new(time, model, LogParsers.ProjectName(e.Str("cwd")), u.Num("input_tokens"), u.Num("output_tokens"),
                u.Num("cache_read_input_tokens"), u.Num("cache_creation_input_tokens"), 0, e.Get("isSidechain").ValueKind == JsonValueKind.True);
        }
    }
}

/// <summary>Codex: ~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl ({timestamp, type, payload}).</summary>
class CodexSessionParser : ILogParser
{
    public string Format => "codex-session";

    public Session Parse(IEnumerable<string> lines)
    {
        var list = new List<LogEntry>(); var models = new HashSet<string>(); long inTok = 0, outTok = 0;
        foreach (var e in LogParsers.Json(lines))
        {
            var p = e.Get("payload"); var time = e.Time();
            switch (e.Str("type"))
            {
                case "turn_context" when p.Str("model") != "": models.Add(p.Str("model")); break;
                case "event_msg" when p.Str("type") == "token_count":
                    var total = p.Get("info").Get("total_token_usage");   // cumulative: keep the latest
                    if (total.ValueKind == JsonValueKind.Object) { inTok = total.Num("input_tokens"); outTok = total.Num("output_tokens"); }
                    break;
                case "response_item":
                    switch (p.Str("type"))
                    {
                        case "message":
                            var text = LogParsers.Text(p.Get("content"));
                            var role = p.Str("role");
                            bool context = role is "developer" or "system" || text.TrimStart().StartsWith("<environment_context>") || text.TrimStart().StartsWith("<user_instructions>");
                            if (text.Trim() != "") list.Add(new(context ? "system" : role == "assistant" ? "assistant" : "user", text, time, false));
                            break;
                        case "reasoning":
                            var sum = string.Join("\n", p.Get("summary") is { ValueKind: JsonValueKind.Array } s ? s.EnumerateArray().Select(x => x.Str("text")) : Array.Empty<string>());
                            if (sum != "") list.Add(new("thinking", sum, time, false));
                            break;
                        case "function_call": list.Add(LogParsers.ToolCall(p.Str("name"), p.Get("arguments"), time, false)); break;
                        case "custom_tool_call":
                            list.Add(new("tool", LogParsers.Cut(p.Str("input"), 300), time, false, p.Str("name"), p.Str("name") == "apply_patch" ? ".diff" : ".txt", p.Str("input")));
                            break;
                        case "function_call_output" or "custom_tool_call_output":
                            var o = p.Get("output");
                            var outText = LogParsers.Cut(o.ValueKind == JsonValueKind.String ? o.GetString()! : o.ValueKind == JsonValueKind.Undefined ? "" : o.Str("output") is { Length: > 0 } x ? x : o.GetRawText(), 20000);
                            list.Add(new("result", outText, time, false, null, LogParsers.Detect(outText, null)));
                            break;
                    }
                    break;
            }
        }
        return LogParsers.Build(list, models, inTok, outTok);
    }

    /// <summary>Token counts are running totals: each event is the difference from the one before. Cached input is part of input, so it is split out.</summary>
    public IEnumerable<UsageEvent> Usage(IEnumerable<string> lines)
    {
        string model = "", project = "";
        long pIn = 0, pOut = 0, pCache = 0, pReason = 0, pWrite = 0;
        foreach (var e in LogParsers.Json(lines))
        {
            var p = e.Get("payload");
            switch (e.Str("type"))
            {
                case "session_meta": if (p.Str("cwd") != "") project = LogParsers.ProjectName(p.Str("cwd")); break;
                case "turn_context":
                    if (p.Str("model") != "") model = p.Str("model");
                    if (project == "" && p.Str("cwd") != "") project = LogParsers.ProjectName(p.Str("cwd"));
                    break;
                case "event_msg" when p.Str("type") == "token_count":
                    var total = p.Get("info").Get("total_token_usage");
                    if (total.ValueKind != JsonValueKind.Object || e.Time() is not { } time) break;
                    long i = total.Num("input_tokens"), o = total.Num("output_tokens"), c = total.Num("cached_input_tokens"), r = total.Num("reasoning_output_tokens"), cw = total.Num("cache_write_input_tokens");
                    if (i < pIn || o < pOut) pIn = pOut = pCache = pReason = pWrite = 0;   // the counter restarted
                    long dIn = i - pIn, dOut = o - pOut, dCache = c - pCache, dReason = r - pReason, dWrite = cw - pWrite;
                    pIn = i; pOut = o; pCache = c; pReason = r; pWrite = cw;
                    if (dIn + dOut <= 0) break;
                    yield return new(time, model == "" ? "codex" : model, project == "" ? "(unknown)" : project, Math.Max(0, dIn - dCache - dWrite), dOut, dCache, Math.Max(0, dWrite), Math.Max(0, dReason), false);
                    break;
            }
        }
    }

    /// <summary>The 5-hour ("primary") and weekly ("secondary") limit readings in token_count events: used percent and when the window resets.</summary>
    public IEnumerable<LimitSample> Limits(IEnumerable<string> lines)
    {
        foreach (var e in LogParsers.Json(lines))
        {
            var p = e.Get("payload");
            if (e.Str("type") != "event_msg" || p.Str("type") != "token_count" || e.Time() is not { } time) continue;
            foreach (var key in new[] { "primary", "secondary" })
            {
                var w = p.Get("rate_limits").Get(key);
                if (w.ValueKind != JsonValueKind.Object || w.Get("used_percent").ValueKind != JsonValueKind.Number) continue;
                int minutes = (int)w.Num("window_minutes");
                var reset = DateTimeOffset.FromUnixTimeSeconds(w.Num("resets_at")).LocalDateTime;
                yield return new(time, minutes == 300 ? "5-hour" : minutes == 10080 ? "Weekly" : $"{minutes}-minute", w.Get("used_percent").GetDouble(), minutes, reset);
            }
        }
    }
}
