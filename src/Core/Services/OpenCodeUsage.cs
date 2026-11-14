// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using Microsoft.Data.Sqlite;

namespace AiStackprint.Services;

/// <summary>
/// OpenCode: ~/.local/share/opencode/opencode.db (SQLite, opened read-only). Usage only: one event per assistant message in
/// <c>message</c>, and one per session in <c>session_v2</c>, where newer versions keep no per-message tokens.
/// OpenCode's output excludes reasoning, so reasoning is added to it. Its cost is used only above 0: it writes 0 when it has no price.
/// </summary>
internal class OpenCodeUsage : ILogParser
{
    public string Format => "opencode-db";
    public bool WholeFile => true;

    /// <summary>Listed as a usage source only, never on the Logs page.</summary>
    public Session Parse(IEnumerable<string> lines) => LogParsers.Build([], [], 0, 0);

    public IEnumerable<UsageEvent> UsageFile(string path)
    {
        var list = new List<UsageEvent>();
        try
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            db.Open();
            var tables = new HashSet<string>();
            using (var r = Query(db, "select name from sqlite_master where type = 'table'"))
            {
                while (r.Read())
                {
                    tables.Add(r.GetString(0));
                }
            }

            var sub = new Dictionary<string, bool>();   // session id -> sub-agent session
            if (tables.Contains("session"))
            {
                using var r = Query(db, "select id, parent_id is not null from session");
                while (r.Read())
                {
                    sub[r.GetString(0)] = r.GetBoolean(1);
                }
            }

            if (tables.Contains("message"))
            {
                using var r = Query(db, "select session_id, time_created, data from message");
                while (r.Read())
                {
                    JsonElement m;
                    try { using var d = JsonDocument.Parse(r.GetString(2)); m = d.RootElement.Clone(); }
                    catch (JsonException) { continue; }
                    var t = m.Get("tokens");
                    if (m.Str("role") != "assistant" || t.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    Add(list, r.GetInt64(1), m.Str("modelID"), m.Get("path").Str("cwd"), t.Num("input"), t.Num("output"), t.Num("reasoning"),
                        t.Get("cache").Num("read"), t.Get("cache").Num("write"), m.Get("cost") is { ValueKind: JsonValueKind.Number } c ? c.GetDouble() : 0, sub.GetValueOrDefault(r.GetString(0)));
                }
            }

            if (tables.Contains("session_v2"))
            {
                using var r = Query(db, "select id, parent_id is not null, directory, model, cost, tokens_input, tokens_output, tokens_reasoning, tokens_cache_read, tokens_cache_write, time_updated from session_v2");
                while (r.Read())
                {
                    if (sub.ContainsKey(r.GetString(0)))
                    {
                        continue;   // also in the old table: already counted per message
                    }

                    var model = "";
                    if (!r.IsDBNull(3))
                    {
                        try { using var d = JsonDocument.Parse(r.GetString(3)); model = d.RootElement.Str("id"); }
                        catch (JsonException) { }
                    }

                    Add(list, L(r, 10), model, r.IsDBNull(2) ? "" : r.GetString(2), L(r, 5), L(r, 6), L(r, 7), L(r, 8), L(r, 9), r.IsDBNull(4) ? 0 : r.GetDouble(4), r.GetBoolean(1));
                }
            }
        }
        catch (SqliteException) { }   // locked, not a database, or another schema: keep what was read
        return list;
    }

    private static SqliteDataReader Query(SqliteConnection db, string sql)
    {
        var cmd = db.CreateCommand();   // not disposed here: that would finalize the statement under the open reader
        cmd.CommandText = sql;
        return cmd.ExecuteReader();
    }

    private static long L(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);

    private static void Add(List<UsageEvent> list, long ms, string model, string cwd, long input, long output, long reasoning, long read, long write, double cost, bool sub)
    {
        if (input + output + reasoning + read + write <= 0)
        {
            return;
        }

        list.Add(new(DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime, model == "" ? "opencode" : model, LogParsers.ProjectName(cwd),
            input, output + reasoning, read, write, reasoning, sub, cost > 0 ? (decimal)cost : null));
    }
}
