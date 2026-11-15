// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>Claude Code: ~/.claude/projects/&lt;project&gt;/&lt;session&gt;.jsonl (sub-agent steps have isSidechain: true).</summary>
internal class ClaudeSessionParser : ILogParser
{
    public string Format => "claude-session";

    public Session Parse(IEnumerable<string> lines)
    {
        var list = new List<LogEntry>(); var models = new HashSet<string>(); long inTok = 0, outTok = 0;
        var seen = new HashSet<string>();   // a streamed reply is written on several lines with the same id and usage: count it once
        var calls = new Dictionary<string, (string Tool, string? File)>();   // tool_use id -> name, file type it touched
        var start = 0; var agent = "";
        void Tag() { if (agent != "") { for (var k = start; k < list.Count; k++) { list[k] = list[k] with { Agent = agent }; } } start = list.Count; }   // sub-agent id on the steps of the previous line
        foreach (var e in LogParsers.Json(lines))
        {
            Tag(); agent = e.Str("agentId");
            var type = e.Str("type"); var time = e.Time();
            var sub = e.Get("isSidechain").ValueKind == JsonValueKind.True;
            if (type == "summary") { list.Add(new("system", "Summary: " + e.Str("summary"), time, sub)); continue; }
            if (type is not ("user" or "assistant"))
            {
                continue;
            }

            var msg = e.Get("message");
            if (msg.Str("model") is { Length: > 0 } m && m != "<synthetic>")
            {
                models.Add(m);
            }

            if (msg.Str("id") is not { Length: > 0 } mid || seen.Add(mid + ":" + e.Str("requestId")))
            { inTok += msg.Get("usage").Num("input_tokens"); outTok += msg.Get("usage").Num("output_tokens"); }
            var content = msg.Get("content");
            if (content.ValueKind == JsonValueKind.String) { list.Add(new(type, content.GetString()!, time, sub)); continue; }
            if (content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var b in content.EnumerateArray())
            {
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
        }
        Tag();
        return LogParsers.Build(list, models, inTok, outTok);
    }

    public IEnumerable<UsageEvent> Usage(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>();
        foreach (var e in LogParsers.Json(lines, "\"usage\""))   // only replies carry usage; tool results and prompts are most of the bytes
        {
            if (e.Str("type") != "assistant")
            {
                continue;
            }

            var msg = e.Get("message"); var u = msg.Get("usage");
            if (u.ValueKind != JsonValueKind.Object || e.Time() is not { } time)
            {
                continue;
            }

            if (msg.Str("id") is { Length: > 0 } id && !seen.Add(id + ":" + e.Str("requestId")))
            {
                continue;   // streamed reply: count once
            }

            var model = msg.Str("model");
            if (model is "" or "<synthetic>")
            {
                continue;
            }

            yield return new(time, model, LogParsers.ProjectName(e.Str("cwd")), u.Num("input_tokens"), u.Num("output_tokens"),
                u.Num("cache_read_input_tokens"), u.Num("cache_creation_input_tokens"), 0, e.Get("isSidechain").ValueKind == JsonValueKind.True);
        }
    }
}
