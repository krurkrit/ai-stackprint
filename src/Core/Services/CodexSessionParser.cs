// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>Codex: ~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl ({timestamp, type, payload}).</summary>
internal class CodexSessionParser : ILogParser
{
    public string Format => "codex-session";

    public Session Parse(IEnumerable<ReadOnlyMemory<byte>> lines)
    {
        var feed = new SessionFeed();
        feed.Add(lines);
        return feed.Build();
    }

    public ISessionFeed Feed() => new SessionFeed();

    /// <summary>The parse, kept between pieces of the file: the token count that follows a reply is put on its first step, even when it is written later.</summary>
    private sealed class SessionFeed : ISessionFeed
    {
        private readonly List<LogEntry> _list = [];
        private readonly HashSet<string> _models = [];
        private readonly LogParsers.LineCounter _lines = new();
        private long _in, _out, _peak, _window;
        private string _model = "";
        private int _firstSince;   // the steps since the last token count are the reply it counts

        public int Add(IEnumerable<ReadOnlyMemory<byte>> lines)
        {
            var from = _list.Count;
            foreach (var e in LogParsers.Json(lines, _lines))
            {
                if (Line(e) is { } changed)
                {
                    from = Math.Min(from, changed);
                }
            }

            return from;
        }

        public Session Snapshot() => LogParsers.Build([.. _list], [.. _models], _in, _out, _peak, _window);

        public Session Build() => LogParsers.Build(_list, _models, _in, _out, _peak, _window);

        /// <returns>The index of an earlier step this line changed (its reply's tokens), or null.</returns>
        private int? Line(JsonElement e)
        {
            var p = e.Get("payload");
            var time = e.Time();
            switch (e.Str("type"))
            {
                case "turn_context" when p.Str("model") != "":
                    _models.Add(p.Str("model"));
                    _model = p.Str("model");
                    break;
                case "event_msg" when p.Str("type") == "token_count":
                    return TokenCount(p.Get("info"), time);
                case "response_item":
                    Item(p, time);
                    break;
            }

            return null;
        }

        private int? TokenCount(JsonElement info, DateTime? time)
        {
            int? changed = null;
            var total = info.Get("total_token_usage");   // cumulative: keep the latest
            var grew = total.ValueKind == JsonValueKind.Object && (total.Num("input_tokens") != _in || total.Num("output_tokens") != _out);   // the same count is written again: not a new reply
            if (total.ValueKind == JsonValueKind.Object)
            {
                _in = total.Num("input_tokens");
                _out = total.Num("output_tokens");
            }

            if (grew && info.Get("last_token_usage") is { ValueKind: JsonValueKind.Object } last && time is { } at)
            {
                var at1 = _list.FindIndex(_firstSince, x => x.Role is "assistant" or "thinking" or "tool");
                if (at1 >= 0)
                {
                    long cached = last.Num("cached_input_tokens");
                    _list[at1] = _list[at1] with
                    {
                        Usage = new(at, _model == "" ? "codex" : _model, "", Math.Max(0, last.Num("input_tokens") - cached), last.Num("output_tokens"), cached, 0, last.Num("reasoning_output_tokens"), false)
                    };
                    changed = at1;
                }
            }

            _firstSince = _list.Count;
            _peak = Math.Max(_peak, info.Get("last_token_usage").Num("input_tokens"));   // one reply's input, cached part included
            _window = info.Num("model_context_window") is > 0 and var w ? w : _window;
            return changed;
        }

        private void Item(JsonElement p, DateTime? time)
        {
            switch (p.Str("type"))
            {
                case "message":
                    var text = LogParsers.Text(p.Get("content"));
                    var role = p.Str("role");
                    var context = role is "developer" or "system" || text.TrimStart().StartsWith("<environment_context>") || text.TrimStart().StartsWith("<user_instructions>");
                    if (text.Trim() != "")
                    {
                        _list.Add(new(context ? "system" : role == "assistant" ? "assistant" : "user", text, time, false));
                    }

                    break;
                case "reasoning":
                    var sum = string.Join("\n", p.Get("summary") is { ValueKind: JsonValueKind.Array } s ? s.EnumerateArray().Select(x => x.Str("text")) : []);
                    if (sum != "")
                    {
                        _list.Add(new("thinking", sum, time, false));
                    }

                    break;
                case "function_call":
                    _list.Add(LogParsers.ToolCall(p.Str("name"), p.Get("arguments"), time, false));
                    break;
                case "custom_tool_call":
                    _list.Add(new("tool", LogParsers.Cut(p.Str("input"), 300), time, false, p.Str("name"), p.Str("name") == "apply_patch" ? ".diff" : ".txt", p.Str("input")));
                    break;
                case "function_call_output" or "custom_tool_call_output":
                    var o = p.Get("output");
                    var outText = LogParsers.Cut(o.ValueKind == JsonValueKind.String ? o.GetString()! : o.ValueKind == JsonValueKind.Undefined ? "" : o.Str("output") is { Length: > 0 } x ? x : o.GetRawText(), 20000);
                    _list.Add(new("result", outText, time, false, null, LogParsers.Detect(outText, null)));
                    break;
            }
        }
    }

    /// <summary>The steps <see cref="Parse"/> would make, only counted: tool output is not copied or checked. Codex has no sub-agent steps.</summary>
    public SessionCounts Count(IEnumerable<ReadOnlyMemory<byte>> lines)
    {
        int prompts = 0, calls = 0;
        long inTok = 0, outTok = 0;
        var models = new HashSet<string>();
        var tools = new Dictionary<string, int>();
        foreach (var e in LogParsers.Json(lines))
        {
            var p = e.Get("payload");
            if (e.Is("type", "turn_context") && p.Str("model") is { Length: > 0 } model)
            {
                models.Add(model);
            }
            else if (e.Is("type", "event_msg") && p.Is("type", "token_count") && p.Get("info").Get("total_token_usage") is { ValueKind: JsonValueKind.Object } total)
            {
                inTok = total.Num("input_tokens");
                outTok = total.Num("output_tokens");
            }
            else if (e.Is("type", "response_item") && p.Is("type", "message") && p.Str("role") is not ("assistant" or "developer" or "system"))
            {
                var text = LogParsers.Text(p.Get("content")).TrimStart();
                prompts += text != "" && !text.StartsWith("<environment_context>") && !text.StartsWith("<user_instructions>") ? 1 : 0;
            }
            else if (e.Is("type", "response_item") && (p.Is("type", "function_call") || p.Is("type", "custom_tool_call")))
            {
                calls++;
                var name = p.Str("name");
                tools[name] = tools.GetValueOrDefault(name) + 1;
            }
        }
        return new(prompts, calls, 0, inTok, outTok, tools, models);
    }

    /// <summary>Token counts are running totals: each event is the difference from the one before. Cached input is part of input, so it is split out.</summary>
    public IEnumerable<UsageEvent> Usage(IEnumerable<ReadOnlyMemory<byte>> lines)
    {
        string model = "", project = "";
        long pIn = 0, pOut = 0, pCache = 0, pReason = 0, pWrite = 0;
        foreach (var e in LogParsers.Json(lines, "session_meta", "turn_context", "token_count"))   // the only lines read below
        {
            var p = e.Get("payload");
            switch (e.Str("type"))
            {
                case "session_meta":
                    if (p.Str("cwd") != "") { project = LogParsers.ProjectName(p.Str("cwd")); }
                    break;
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
                    pIn = i;
                    pOut = o;
                    pCache = c;
                    pReason = r;
                    pWrite = cw;
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
    public IEnumerable<LimitSample> Limits(IEnumerable<ReadOnlyMemory<byte>> lines)
    {
        foreach (var e in LogParsers.Json(lines, "token_count"))
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

                var minutes = (int)w.Num("window_minutes");
                var reset = DateTimeOffset.FromUnixTimeSeconds(w.Num("resets_at")).LocalDateTime;
                yield return new(time, minutes == 300 ? "5-hour" : minutes == 10080 ? "Weekly" : $"{minutes}-minute", w.Get("used_percent").GetDouble(), minutes, reset);
            }
        }
    }
}
