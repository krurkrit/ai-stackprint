// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// A session log that is being written (Follow): read once like <see cref="SessionAgents.Read"/> (its newest bytes, with the sub-agents' own files), then
/// <see cref="Next"/> reads only the bytes written to each file since and parses only those, keeping the parse between pieces (<see cref="ISessionFeed"/>).
/// A file that shrank or was replaced, an earlier step that changed, or more than the newest bytes: it is read again from the start. Not thread-safe: one call at a time.
/// </summary>
public sealed class SessionTail
{
    /// <summary>What <see cref="Next"/> found: the session now, the steps added at its end, or <paramref name="Rebuilt"/> when it was read again (show it afresh).</summary>
    public sealed record Update(Session Session, List<LogEntry> Added, bool Rebuilt);

    private sealed class Part(string path, ISessionFeed feed)
    {
        public string Path { get; } = path;
        public ISessionFeed Feed { get; } = feed;
        public long End { get; set; }        // the byte the next read starts at
        public bool InLine { get; set; }     // that byte is inside a line
        public int Steps { get; set; }       // steps the feed has
        public Session? Parsed { get; set; } // its last copy
        public bool Lines { get; init; } = true;   // read whole: the steps keep their lines (an agent file read cut to its end has no reliable lines)
        public bool Shown { get; set; }      // an agent with steps in the part of the session read
    }

    private readonly LogFile _file;
    private readonly long _max;
    private readonly ILogParser _parser;
    private readonly Dictionary<string, Part> _agents = new(StringComparer.OrdinalIgnoreCase);
    private Part _main = null!;
    private long _mainFrom;     // where the read of the main file started
    private DateTime? _from;    // a cut read: agents' steps before the first step read are left out

    private SessionTail(LogFile f, long maxBytes, ILogParser parser)
    {
        _file = f;
        _max = maxBytes;
        _parser = parser;
    }

    /// <summary>The session as read so far.</summary>
    public Session Session { get; private set; } = null!;

    /// <summary>The main file was read cut to its newest bytes: the steps' lines count from where the read started, not from the file's first line.</summary>
    public bool Cut { get; private set; }

    /// <summary>Reads the session; null for a format whose parse cannot be given lines in pieces.</summary>
    public static SessionTail? Open(LogFile f, long maxBytes)
    {
        if (LogParsers.Find(f.Format) is not { } parser || parser.Feed() == null)
        {
            return null;
        }

        var tail = new SessionTail(f, maxBytes, parser);
        tail.ReadAll();
        return tail;
    }

    /// <summary>Reads what was written since the last read.</summary>
    public Update Next()
    {
        var size = new FileInfo(_file.Path).Length;
        if (size < _main.End || size - _mainFrom > 2 * _max)
        {
            return Again();   // replaced, or what is kept has grown far past the newest bytes
        }

        var before = _main.Steps;
        if (Read(_main, _main.End, _main.InLine) < before)
        {
            return Again();
        }

        var main = _main.Parsed!.Entries.Skip(before).ToList();
        var sub = new List<LogEntry>();
        if (_file.Format == "claude-session")
        {
            foreach (var file in SessionAgents.AgentFiles(_file.Path))
            {
                if (!More(file, sub))
                {
                    return Again();
                }
            }
        }

        if (main.Count == 0 && sub.Count == 0)
        {
            Session = Totals(Session.Entries);   // tokens may have changed without a step
            return new(Session, [], false);
        }

        var added = SessionAgents.ByTime(main, sub);
        Session = Totals([.. Session.Entries, .. added]);
        return new(Session, added, false);
    }

    private Update Again()
    {
        ReadAll();
        return new(Session, [], true);
    }

    private void ReadAll()
    {
        var size = new FileInfo(_file.Path).Length;
        Cut = size > _max;
        _mainFrom = Cut ? size - _max : 0;
        _main = new Part(_file.Path, _parser.Feed()!);
        Read(_main, _mainFrom, Cut);
        _from = Cut ? _main.Parsed!.Entries.FirstOrDefault(e => e.Time != null)?.Time : null;
        _agents.Clear();
        var sub = new List<LogEntry>();
        if (_file.Format == "claude-session")
        {
            foreach (var file in SessionAgents.AgentFiles(_file.Path))
            {
                More(file, sub);
            }
        }

        Session = Totals(sub.Count == 0 ? [.. _main.Parsed!.Entries] : SessionAgents.ByTime(_main.Parsed!.Entries, sub));
    }

    /// <summary>The new steps of an agent file (all of a new one) added to <paramref name="sub"/>; false when the file shrank or an earlier step changed.</summary>
    private bool More(string file, List<LogEntry> sub)
    {
        try
        {
            var size = new FileInfo(file).Length;
            if (!_agents.TryGetValue(file, out var p))
            {
                p = new Part(file, _parser.Feed()!) { Lines = size <= SessionAgents.AgentBytes };
                _agents[file] = p;
                Read(p, p.Lines ? 0 : size - SessionAgents.AgentBytes, !p.Lines);
                sub.AddRange(Steps(p, 0));
                return true;
            }

            if (size == p.End)
            {
                return true;
            }

            if (size < p.End || size - p.End > SessionAgents.AgentBytes)
            {
                return false;
            }

            var before = p.Steps;
            if (Read(p, p.End, p.InLine) < before)
            {
                return false;
            }

            sub.AddRange(Steps(p, before));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;   // an agent file that vanished or is locked is left out, as in SessionAgents.Read
        }
    }

    /// <returns>The index of the first new or changed step of the part.</returns>
    private static int Read(Part p, long from, bool dropFirst)
    {
        var piece = LogReader.CompleteLines(p.Path, from, dropFirst);
        p.End = piece.End;
        p.InLine = piece.InLine;
        if (piece.Rows.Count == 0)
        {
            p.Parsed ??= p.Feed.Snapshot();
            return p.Steps;
        }

        var first = p.Feed.Add(piece.Rows);
        p.Parsed = p.Feed.Snapshot();
        p.Steps = p.Parsed.Entries.Count;
        return first;
    }

    /// <summary>An agent's steps from index <paramref name="from"/>, marked as its sub-agent steps; those before a cut read's first step are left out.</summary>
    private List<LogEntry> Steps(Part p, int from)
    {
        var id = Path.GetFileNameWithoutExtension(p.Path)["agent-".Length..];
        var steps = p.Parsed!.Entries.Skip(from)
            .Where(e => _from == null || e.Time == null || e.Time >= _from)
            .Select(e => e with { SubAgent = true, Agent = string.IsNullOrEmpty(e.Agent) ? id : e.Agent, Source = p.Lines ? p.Path : null, Line = p.Lines ? e.Line : 0 })
            .ToList();
        p.Shown |= steps.Count > 0;
        return steps;
    }

    /// <summary>The session's counts over the main file and the agents shown, as <see cref="SessionAgents.Merge"/> makes them.</summary>
    private Session Totals(List<LogEntry> entries)
    {
        var main = _main.Parsed!;
        var shown = _agents.Values.Where(a => a.Shown).Select(a => a.Parsed!).ToList();
        var before = _agents.Values.Count(a => !a.Shown && a.Steps > 0);   // agents that ran before the part of the session read
        if (shown.Count == 0)
        {
            return main with { Entries = entries, AgentsBefore = before };
        }

        var times = entries.Where(e => e.SubAgent && e.Time != null).Select(e => e.Time).Append(main.Start).Append(main.End).Where(t => t != null).ToList();
        return main with
        {
            Entries = entries,
            Models = [.. main.Models.Concat(shown.SelectMany(s => s.Models))],
            InputTokens = main.InputTokens + shown.Sum(s => s.InputTokens),
            OutputTokens = main.OutputTokens + shown.Sum(s => s.OutputTokens),
            AgentsBefore = before,
            Start = times.Min(),
            End = times.Max()
        };
    }
}
