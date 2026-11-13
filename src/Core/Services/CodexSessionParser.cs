// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

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
                            if (text.Trim() != "")
                            {
                                list.Add(new(context ? "system" : role == "assistant" ? "assistant" : "user", text, time, false));
                            }

                            break;
                        case "reasoning":
                            var sum = string.Join("\n", p.Get("summary") is { ValueKind: JsonValueKind.Array } s ? s.EnumerateArray().Select(x => x.Str("text")) : Array.Empty<string>());
                            if (sum != "")
                            {
                                list.Add(new("thinking", sum, time, false));
                            }

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
                case "session_meta": if (p.Str("cwd") != "") { project = LogParsers.ProjectName(p.Str("cwd")); } break;
                case "turn_context":
                    if (p.Str("model") != "")
                    {
                        model = p.Str("model");
                    }

                    if (project == "" && p.Str("cwd") != "")
                    {
                        project = LogParsers.ProjectName(p.Str("cwd"));
                    }

                    break;
                case "event_msg" when p.Str("type") == "token_count":
                    var total = p.Get("info").Get("total_token_usage");
                    if (total.ValueKind != JsonValueKind.Object || e.Time() is not { } time)
                    {
                        break;
                    }

                    long i = total.Num("input_tokens"), o = total.Num("output_tokens"), c = total.Num("cached_input_tokens"), r = total.Num("reasoning_output_tokens"), cw = total.Num("cache_write_input_tokens");
                    if (i < pIn || o < pOut)
                    {
                        pIn = pOut = pCache = pReason = pWrite = 0;   // the counter restarted
                    }

                    long dIn = i - pIn, dOut = o - pOut, dCache = c - pCache, dReason = r - pReason, dWrite = cw - pWrite;
                    pIn = i; pOut = o; pCache = c; pReason = r; pWrite = cw;
                    if (dIn + dOut <= 0)
                    {
                        break;
                    }

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
            if (e.Str("type") != "event_msg" || p.Str("type") != "token_count" || e.Time() is not { } time)
            {
                continue;
            }

            foreach (var key in new[] { "primary", "secondary" })
            {
                var w = p.Get("rate_limits").Get(key);
                if (w.ValueKind != JsonValueKind.Object || w.Get("used_percent").ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                int minutes = (int)w.Num("window_minutes");
                var reset = DateTimeOffset.FromUnixTimeSeconds(w.Num("resets_at")).LocalDateTime;
                yield return new(time, minutes == 300 ? "5-hour" : minutes == 10080 ? "Weekly" : $"{minutes}-minute", w.Get("used_percent").GetDouble(), minutes, reset);
            }
        }
    }
}
