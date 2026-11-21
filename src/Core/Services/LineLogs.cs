// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// Text logs: one line per event, with a time, a level and a message (<c>[2026-08-07 15:23:49] [info] message</c>, key=value, JSON per line, glog).
/// An app's log source opts in with <c>"format": "text-log"</c>; the line format is found from the first lines of each file, so a folder that mixes formats still reads.
/// A line that starts none of them (a stack trace, the rest of a multi-line message) joins the line above. The result is a <see cref="Session"/> whose
/// steps have the level as their role (error, warn, info, debug, trace, log), so search, <c>role:error</c> and the viewer's other tools work as they do for sessions.
/// </summary>
public static partial class LineLogs
{
    /// <summary>The <c>format</c> value in apps.json.</summary>
    public const string Format = "text-log";

    private static readonly string[] _levels = ["error", "warn", "info", "debug", "trace", "log"];

    public static bool Is(string? format) => format == Format;

    /// <summary>True for the role of a step from a text log (not a session step).</summary>
    public static bool IsLevel(string role) => Array.IndexOf(_levels, role) >= 0;

    private sealed record Parsed(DateTime? Time, string Level, string Message, string? Detail)
    {
        public long Line { get; set; }
    }

    private delegate Parsed? LineParser(string line);

    private static readonly LineParser[] _parsers = [Bracketed, Logfmt, JsonLine, Glog, Span, EventJson];

    /// <summary>Reads the lines of a text log into steps.</summary>
    public static Session Parse(IEnumerable<ReadOnlyMemory<byte>> lines)
    {
        var text = lines.Select(l => Encoding.UTF8.GetString(l.Span).TrimEnd('\r')).ToList();
        var parse = Detect(text);
        var entries = new List<LogEntry>();
        List<string>? more = null;   // continuation lines of the step being built
        Parsed? open = null;

        void Close()
        {
            if (open == null)
            {
                return;
            }

            var message = more == null ? open.Message : open.Message + "\n" + string.Join("\n", more);
            entries.Add(new LogEntry(open.Level, message, open.Time, false, Detail: open.Detail) { Line = open.Line });
            open = null;
            more = null;
        }

        var n = 0;
        foreach (var line in text)
        {
            n++;
            var p = parse == null ? null : parse(line);
            if (p != null)
            {
                Close();
                open = p;
                p.Line = n;
            }
            else if (open != null && line.Length > 0)
            {
                (more ??= []).Add(line);
            }
        }

        Close();
        return new Session(entries, [], 0, 0, entries.FirstOrDefault(e => e.Time != null)?.Time, entries.LastOrDefault(e => e.Time != null)?.Time);
    }

    /// <summary>The line format most of the first lines have, or null when none fits.</summary>
    private static LineParser? Detect(List<string> text)
    {
        var sample = text.Where(l => l.Length > 0).Take(60).ToList();
        LineParser? best = null;
        var most = 0;
        foreach (var p in _parsers)
        {
            var n = sample.Count(l => p(l) != null);
            if (n > most)
            {
                most = n;
                best = p;
            }
        }

        return best;
    }

    private static string NormalizeLevel(string s) => s.ToUpperInvariant() switch
    {
        "ERROR" or "ERR" or "FATAL" or "CRITICAL" or "SEVERE" or "E" or "F" => "error",
        "WARN" or "WARNING" or "W" => "warn",
        "INFO" or "NOTICE" or "I" => "info",
        "DEBUG" or "D" => "debug",
        "TRACE" or "VERBOSE" or "T" => "trace",
        _ => "log"
    };

    private static DateTime? ParseTime(string s) =>
        DateTimeOffset.TryParse(s.Replace(',', '.'), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d.LocalDateTime : null;

    // 2026-07-10 15:39:29.124 [info] message · [2026-08-07 15:23:49.447] [info]  message · 2026-11-19T15:29:00.045Z [INFO] message
    [GeneratedRegex(@"^\[?(?<t>\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d(?:[.,]\d+)?Z?)\]?\s+\[(?<l>[A-Za-z]+)\]\s*(?<m>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedLine();

    private static Parsed? Bracketed(string line)
    {
        var m = BracketedLine().Match(line);
        return m.Success ? new Parsed(ParseTime(m.Groups["t"].Value), NormalizeLevel(m.Groups["l"].Value), m.Groups["m"].Value, null) : null;
    }

    // timestamp=2026-06-17T10:56:15.601Z level=INFO run=a0aee087 message="creating instance" directory=...
    [GeneratedRegex(@"^timestamp=(?<t>\S+)\s+level=(?<l>\w+)\s+(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LogfmtLine();

    [GeneratedRegex(@"message=(?:""(?<q>(?:[^""\\]|\\.)*)""|(?<p>\S+))", RegexOptions.CultureInvariant)]
    private static partial Regex LogfmtMessage();

    private static Parsed? Logfmt(string line)
    {
        var m = LogfmtLine().Match(line);
        if (!m.Success)
        {
            return null;
        }

        var rest = m.Groups["rest"].Value;
        var msg = LogfmtMessage().Match(rest);
        var text = !msg.Success ? "" : msg.Groups["q"].Success ? msg.Groups["q"].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : msg.Groups["p"].Value;
        return new Parsed(ParseTime(m.Groups["t"].Value), NormalizeLevel(m.Groups["l"].Value), text, msg.Success ? rest.Remove(msg.Index, msg.Length).Trim() : rest);
    }

    // {"timestamp":"2026-08-07T08:25:58.775268Z","level":"INFO","fields":{"message":"CLI command executed"},...}
    private static Parsed? JsonLine(string line)
    {
        if (line.Length < 2 || line[0] != '{')
        {
            return null;
        }

        try
        {
            using var d = JsonDocument.Parse(line);
            var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("timestamp", out var t) || t.ValueKind != JsonValueKind.String
                || !r.TryGetProperty("level", out var l) || l.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? message = null;
            if (r.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object && f.TryGetProperty("message", out var fm))
            {
                message = fm.GetString();
            }
            else if (r.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
            {
                message = m.GetString();
            }

            return new Parsed(ParseTime(t.GetString() ?? ""), NormalizeLevel(l.GetString() ?? ""), message ?? "", line);
        }
        catch (JsonException)
        {
            return null;   // cut off or not JSON
        }
    }

    // {"type":"effect-span","name":"git.exec","startTimeUnixNano":"1789486915457000000","durationMs":179,"attributes":{...},"exit":{"_tag":"Success"}}   (Orca's trace)
    private static Parsed? Span(string line)
    {
        if (line.Length < 2 || line[0] != '{' || !line.Contains("\"effect-span\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var d = JsonDocument.Parse(line);
            var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !r.TryGetProperty("startTimeUnixNano", out var start) || !long.TryParse(start.ToString(), out var nanos))
            {
                return null;
            }

            var attributes = r.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
            var what = attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty("breadcrumb.name", out var crumb) ? crumb.GetString()
                : attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty("git.subcommand", out var sub) ? "git " + sub.GetString() : null;
            var failed = r.TryGetProperty("exit", out var exit) && exit.TryGetProperty("_tag", out var tag) && tag.GetString() == "Failure";
            var ms = r.TryGetProperty("durationMs", out var dur) && dur.ValueKind == JsonValueKind.Number ? dur.GetDouble() : 0;
            var text = name.GetString() + (what != null ? " · " + what : "") + (ms >= 1 ? $" · {ms:0} ms" : "");
            if (failed && exit.TryGetProperty("cause", out var cause) && cause.ValueKind == JsonValueKind.String)
            {
                text += '\n' + cause.GetString();
            }

            var level = failed ? "error" : name.GetString()!.Contains("breadcrumb", StringComparison.Ordinal) ? "debug" : "info";
            return new Parsed(DateTimeOffset.FromUnixTimeMilliseconds(nanos / 1_000_000).LocalDateTime, level, text, line);
        }
        catch (JsonException)
        {
            return null;   // cut off or not JSON
        }
    }

    // {"src":"daemon","ts":"2026-09-15T15:42:14.106Z","pid":4580,"event":"startup","protocolVersion":36}   (Orca's daemon log)
    private static Parsed? EventJson(string line)
    {
        if (line.Length < 2 || line[0] != '{' || !line.Contains("\"event\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var d = JsonDocument.Parse(line);
            var r = d.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("ts", out var ts) || ts.ValueKind != JsonValueKind.String
                || !r.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var extra = r.EnumerateObject().Where(p => p.Name is not ("ts" or "event" or "src") && p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                .Take(4).Select(p => $"{p.Name}={p.Value}");
            var name = ev.GetString() ?? "";
            var level = name.Contains("error", StringComparison.OrdinalIgnoreCase) || name.Contains("fail", StringComparison.OrdinalIgnoreCase) ? "error" : "info";
            return new Parsed(ParseTime(ts.GetString() ?? ""), level, (name + " " + string.Join(" ", extra)).TrimEnd(), line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ERROR: logging before google.Init: I1119 22:11:27.954770 1 server.go:2552] message   (no year in the line: the current one, or the last if that would be in the future)
    [GeneratedRegex(@"^(?:ERROR: logging before google\.Init: )?(?<l>[IWEF])(?<mo>\d\d)(?<d>\d\d) (?<t>\d\d:\d\d:\d\d(?:\.\d+)?)\s+\d+ \S+\] (?<m>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex GlogLine();

    private static Parsed? Glog(string line)
    {
        var m = GlogLine().Match(line);
        if (!m.Success)
        {
            return null;
        }

        var now = DateTime.Now;
        var text = $"{now.Year}-{m.Groups["mo"].Value}-{m.Groups["d"].Value} {m.Groups["t"].Value}";
        var time = ParseTime(text);
        if (time > now.AddDays(1))
        {
            time = time?.AddYears(-1);
        }

        return new Parsed(time, NormalizeLevel(m.Groups["l"].Value), m.Groups["m"].Value, null);
    }
}
