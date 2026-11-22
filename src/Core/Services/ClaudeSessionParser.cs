// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace AiStackprint.Services;

/// <summary>Claude Code: ~/.claude/projects/&lt;project&gt;/&lt;session&gt;.jsonl (sub-agent steps have isSidechain: true).</summary>
internal class ClaudeSessionParser : ILogParser
{
    public string Format => "claude-session";

    public Session Parse(IEnumerable<ReadOnlyMemory<byte>> lines)
    {
        var feed = new SessionFeed();
        feed.Add(lines);
        return feed.Build();
    }

    public ISessionFeed Feed() => new SessionFeed();

    /// <summary>The parse, kept between pieces of the file: a tool result written after its call still gets the tool's name, a reply's tokens are counted once.</summary>
    private sealed class SessionFeed : ISessionFeed
    {
        private readonly List<LogEntry> _list = [];
        private readonly HashSet<string> _models = [];
        private readonly HashSet<string> _seen = [];   // a streamed reply is written on several lines with the same id and usage: count it once
        private readonly Dictionary<string, (string Tool, string? File)> _calls = [];   // tool_use id -> name, file type it touched
        private readonly LogParsers.LineCounter _lines = new();
        private long _in, _out, _peak;
        private int _start;
        private string _agent = "";
        private UsageEvent? _pending;   // the reply's usage, put on the first step the reply makes

        public int Add(IEnumerable<ReadOnlyMemory<byte>> lines)
        {
            var from = _list.Count;
            foreach (var e in LogParsers.Json(lines, _lines))
            {
                Tag();
                _agent = e.Str("agentId");
                Line(e);
            }

            Tag();
            return from;   // a piece changes only its own steps
        }

        public Session Snapshot() => LogParsers.Build([.. _list], [.. _models], _in, _out, _peak);

        public List<LogEntry> Take()
        {
            var steps = new List<LogEntry>(_list);
            _list.Clear();
            _start = 0;
            return steps;
        }

        /// <summary>The session, sharing the feed's lists (a parse of the whole file, which is not fed again).</summary>
        public Session Build() => LogParsers.Build(_list, _models, _in, _out, _peak);

        /// <summary>The sub-agent id of the previous line on its steps.</summary>
        private void Tag()
        {
            if (_agent != "")
            {
                for (var k = _start; k < _list.Count; k++)
                {
                    _list[k] = _list[k] with { Agent = _agent };
                }
            }

            _start = _list.Count;
        }

        private void Line(JsonElement e)
        {
            var type = e.Str("type");
            var time = e.Time();
            var sub = e.Get("isSidechain").ValueKind == JsonValueKind.True;
            if (type == "summary")
            {
                _list.Add(new("system", "Summary: " + e.Str("summary"), time, sub));
                return;
            }

            if (type is not ("user" or "assistant"))
            {
                return;
            }

            var msg = e.Get("message");
            if (msg.Str("model") is { Length: > 0 } m && m != "<synthetic>")
            {
                _models.Add(m);
            }

            var before = _list.Count;
            if (msg.Str("id") is not { Length: > 0 } mid || _seen.Add(mid + ":" + e.Str("requestId")))
            {
                var u = msg.Get("usage");
                _pending ??= type == "assistant" ? Reply(e, msg) : null;
                _in += u.Num("input_tokens");
                _out += u.Num("output_tokens");
                if (!sub)
                {
                    _peak = Math.Max(_peak, u.Num("input_tokens") + u.Num("cache_read_input_tokens") + u.Num("cache_creation_input_tokens"));   // the main conversation's context
                }
            }

            var content = msg.Get("content");
            if (content.ValueKind == JsonValueKind.String)
            {
                _list.Add(new(type, content.GetString()!, time, sub));
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in content.EnumerateArray())
                {
                    Block(b, type, time, sub);
                }
            }

            if (_pending != null && _list.Count > before)
            {
                _list[before] = _list[before] with { Usage = _pending };
                _pending = null;
            }
        }

        private void Block(JsonElement b, string type, DateTime? time, bool sub)
        {
            switch (b.Str("type"))
            {
                case "text" when b.Str("text").Trim() != "":
                    _list.Add(new(type, b.Str("text"), time, sub));
                    break;
                case "thinking":
                    _list.Add(new("thinking", b.Str("thinking"), time, sub));
                    break;
                case "tool_use":
                    _calls[b.Str("id")] = (b.Str("name"), LogParsers.FileHint(b.Get("input")));
                    _list.Add(LogParsers.ToolCall(b.Str("name"), b.Get("input"), time, sub));
                    break;
                case "tool_result":
                    var text = LogParsers.Cut(LogParsers.Text(b.Get("content")), 20000);
                    _calls.TryGetValue(b.Str("tool_use_id"), out var call);
                    _list.Add(new LogEntry("result", text, time, sub, call.Tool, LogParsers.Detect(text, call.Tool == "Read" ? call.File : null)) { Error = b.Get("is_error").ValueKind == JsonValueKind.True });
                    break;
            }
        }
    }

    /// <summary>The steps <see cref="Parse"/> would make, only counted: no text is copied, cut or checked for JSON.</summary>
    public SessionCounts Count(IEnumerable<ReadOnlyMemory<byte>> lines)
    {
        int prompts = 0, calls = 0, subSteps = 0;
        long inTok = 0, outTok = 0;
        var models = new HashSet<string>();
        var tools = new Dictionary<string, int>();
        var seen = new HashSet<string>();
        foreach (var e in LogParsers.Json(lines))
        {
            var sub = e.Get("isSidechain").ValueKind == JsonValueKind.True;
            if (e.Is("type", "summary"))
            {
                subSteps += sub ? 1 : 0;
                continue;
            }

            var user = e.Is("type", "user");
            if (!user && !e.Is("type", "assistant"))
            {
                continue;
            }

            var msg = e.Get("message");
            if (msg.Str("model") is { Length: > 0 } m && m != "<synthetic>")
            {
                models.Add(m);
            }

            if (msg.Str("id") is not { Length: > 0 } mid || seen.Add(mid + ":" + e.Str("requestId")))
            {
                inTok += msg.Get("usage").Num("input_tokens");
                outTok += msg.Get("usage").Num("output_tokens");
            }
            var content = msg.Get("content");
            var steps = 0;
            if (content.ValueKind == JsonValueKind.String)
            {
                steps = 1;
                prompts += user ? 1 : 0;
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in content.EnumerateArray())
                {
                    if (b.Is("type", "text") && !string.IsNullOrWhiteSpace(b.Str("text")))
                    {
                        steps++;
                        prompts += user ? 1 : 0;
                    }
                    else if (b.Is("type", "tool_use"))
                    {
                        steps++;
                        calls++;
                        var name = b.Str("name");
                        tools[name] = tools.GetValueOrDefault(name) + 1;
                    }
                    else if (b.Is("type", "thinking") || b.Is("type", "tool_result")) { steps++; }
                }
            }

            subSteps += sub ? steps : 0;
        }
        return new(prompts, calls, subSteps, inTok, outTok, tools, models);
    }

    /// <summary>The usage of one reply line (what changes the price: 1-hour cache writes, fast mode, US-only processing, web searches; missing = standard, global, short cache, none); null without a time, usage or model.</summary>
    private static UsageEvent? Reply(JsonElement e, JsonElement msg)
    {
        var u = msg.Get("usage");
        var model = msg.Str("model");
        if (u.ValueKind != JsonValueKind.Object || e.Time() is not { } time || model is "" or "<synthetic>")
        {
            return null;
        }

        var write = u.Num("cache_creation_input_tokens");
        return new(time, model, LogParsers.ProjectName(e.Str("cwd")), u.Num("input_tokens"), u.Num("output_tokens"),
            u.Num("cache_read_input_tokens"), write, 0, e.Get("isSidechain").ValueKind == JsonValueKind.True,
            CacheWrite1h: Math.Min(write, u.Get("cache_creation").Num("ephemeral_1h_input_tokens")),
            Fast: u.Str("speed") == "fast",
            UsOnly: string.Equals(u.Str("inference_geo"), "us", StringComparison.OrdinalIgnoreCase),
            WebSearches: (int)u.Get("server_tool_use").Num("web_search_requests"));
    }

    public IEnumerable<UsageEvent> Usage(IEnumerable<ReadOnlyMemory<byte>> lines)
    {
        var seen = new HashSet<string>();
        foreach (var e in LogParsers.Json(lines, "\"usage\""))   // only replies carry usage; tool results and prompts are most of the bytes
        {
            if (e.Str("type") != "assistant")
            {
                continue;
            }

            var msg = e.Get("message");
            if (Reply(e, msg) is not { } reply)
            {
                continue;
            }

            if (msg.Str("id") is { Length: > 0 } id && !seen.Add(id + ":" + e.Str("requestId")))
            {
                continue;   // streamed reply: count once
            }

            yield return reply;
        }
    }
}
