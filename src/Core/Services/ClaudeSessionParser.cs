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
        var list = new List<LogEntry>();
        var models = new HashSet<string>();
        long inTok = 0, outTok = 0, peak = 0;
        var seen = new HashSet<string>();   // a streamed reply is written on several lines with the same id and usage: count it once
        var calls = new Dictionary<string, (string Tool, string? File)>();   // tool_use id -> name, file type it touched
        var start = 0;
        var agent = "";
        UsageEvent? pending = null;   // the reply's usage, put on the first step the reply makes
        void Tag()
        {
            if (agent != "") { for (var k = start; k < list.Count; k++) { list[k] = list[k] with { Agent = agent }; } }
            start = list.Count;
        }   // sub-agent id on the steps of the previous line
        foreach (var e in LogParsers.Json(lines))
        {
            Tag();
            agent = e.Str("agentId");
            var type = e.Str("type");
            var time = e.Time();
            var sub = e.Get("isSidechain").ValueKind == JsonValueKind.True;
            if (type == "summary")
            {
                list.Add(new("system", "Summary: " + e.Str("summary"), time, sub));
                continue;
            }
            if (type is not ("user" or "assistant"))
            {
                continue;
            }

            var msg = e.Get("message");
            if (msg.Str("model") is { Length: > 0 } m && m != "<synthetic>")
            {
                models.Add(m);
            }

            var before = list.Count;
            if (msg.Str("id") is not { Length: > 0 } mid || seen.Add(mid + ":" + e.Str("requestId")))
            {
                var u = msg.Get("usage");
                pending ??= type == "assistant" ? Reply(e, msg) : null;
                inTok += u.Num("input_tokens");
                outTok += u.Num("output_tokens");
                if (!sub)
                {
                    peak = Math.Max(peak, u.Num("input_tokens") + u.Num("cache_read_input_tokens") + u.Num("cache_creation_input_tokens"));   // the main conversation's context
                }
            }
            var content = msg.Get("content");
            if (content.ValueKind == JsonValueKind.String)
            {
                list.Add(new(type, content.GetString()!, time, sub));
                Attach();
                continue;
            }
            if (content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var b in content.EnumerateArray())
            {
                switch (b.Str("type"))
                {
                    case "text" when b.Str("text").Trim() != "":
                        list.Add(new(type, b.Str("text"), time, sub));
                        break;
                    case "thinking":
                        list.Add(new("thinking", b.Str("thinking"), time, sub));
                        break;
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

            Attach();
            void Attach()
            {
                if (pending != null && list.Count > before)
                {
                    list[before] = list[before] with { Usage = pending };
                    pending = null;
                }
            }
        }
        Tag();
        return LogParsers.Build(list, models, inTok, outTok, peak);
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
