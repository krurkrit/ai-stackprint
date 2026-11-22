// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>
/// Searches a session log as steps, in the whole file and in its sub-agents' files. A step's text is what is searched, not the JSON around it (a raw search would match the
/// keys of every line), and role and tool filters (<c>role:user</c>, <c>tool:bash</c>) work. Each result is a place, the file (<see cref="SearchHit.Source"/>, null = the
/// session file) and the line the step starts on, which the viewer uses to go there. The file is read once, in the background, and only the results are kept.
/// </summary>
public static class SessionSearch
{
    /// <summary>The steps of the session file and of its agents' files that match; at most <paramref name="max"/> results are returned, and <paramref name="total"/> counts them all.</summary>
    public static List<SearchHit> File(LogFile f, LogQuery query, int max, CancellationToken ct, out int total)
    {
        total = 0;
        var parser = LogParsers.Find(f.Format);
        if (parser == null)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        int count = 0;
        void Scan(string path, string? source, long maxBytes)
        {
            ct.ThrowIfCancellationRequested();
            var steps = parser.Feed() is { } feed ? Steps(feed, path, ct) : parser.Parse(LogReader.Utf8Lines(path, maxBytes)).Entries;
            foreach (var e in steps.Where(query.IsMatch))
            {
                count++;
                if (hits.Count < max && e.Line > 0)
                {
                    hits.Add(Hit(e, query, source));
                }
            }
        }

        Scan(f.Path, null, new FileInfo(f.Path).Length);
        if (f.Format == "claude-session")
        {
            foreach (var agent in SessionAgents.AgentFiles(f.Path))
            {
                var size = new FileInfo(agent).Length;
                if (size <= SessionAgents.AgentBytes)   // a larger one has no reliable lines: it is left out
                {
                    Scan(agent, agent, size);
                }
            }
        }

        total = count;
        return hits;
    }

    /// <summary>Bytes read at a time when a whole file is read as steps.</summary>
    internal const long Batch = 4L << 20;

    /// <summary>
    /// Every step of a session file, read <see cref="Batch"/> bytes at a time: only one batch and its steps are held, not the whole session
    /// (the steps and their lines are those of one parse of the file).
    /// </summary>
    internal static IEnumerable<LogEntry> Steps(ISessionFeed feed, string path, CancellationToken ct) => Steps(feed, path, ct, Batch);

    internal static IEnumerable<LogEntry> Steps(ISessionFeed feed, string path, CancellationToken ct, long batch)
    {
        long at = 0;
        var inLine = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var piece = LogReader.CompleteLines(path, at, inLine, batch);
            if (piece.End == at)
            {
                yield break;
            }

            (at, inLine) = (piece.End, piece.InLine);
            feed.Add(piece.Rows);
            foreach (var e in feed.Take())
            {
                yield return e;
            }
        }
    }

    private static SearchHit Hit(LogEntry e, LogQuery query, string? source)
    {
        var line = $"{e.Text} {e.Detail}".ReplaceLineEndings(" ").Trim();
        var (start, length) = query.Spans(line) is { Count: > 0 } spans ? spans[0] : (0, 0);
        return new SearchHit(e.Line, LogSearch.Snippet(line, start, length), start, length)
        {
            Source = source,
            Caption = $"{e.Time:HH:mm:ss} {e.Role}{(e.Tool is { } t ? " · " + t : "")}"
        };
    }
}
