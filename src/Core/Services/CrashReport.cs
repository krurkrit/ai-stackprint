// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;

namespace AiStackprint.Services;

/// <summary>
/// A GitHub "new issue" link prefilled with the last crash.log entry. Nothing is sent: the link opens in the browser, where the user reads,
/// edits and submits it (or not). The home folder and user name are replaced before the text goes into the link.
/// </summary>
public static partial class CrashReport
{
    /// <summary>Browsers and GitHub refuse very long links; the entry is cut to stay under this.</summary>
    public const int MaxUrl = 7000;

    /// <summary>The newest entry in crash.log (heading line and stack trace); null when there is none.</summary>
    public static string? LastEntry(string? folder = null)
    {
        var path = Path.Combine(folder ?? DataFolder.Logs, "crash.log");
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            var starts = EntryStart().Matches(text);
            return starts.Count == 0 ? null : text[starts[^1].Index..].Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Replaces the home folder with ~ and the user name with &lt;user&gt;, so paths in the stack trace do not name the person.</summary>
    public static string Redact(string text, string home, string user)
    {
        if (home.Length > 1)
        {
            foreach (var h in new[] { home, home.Replace('\\', '/'), home.Replace('/', '\\') }.Distinct())
            {
                text = text.Replace(h, "~", StringComparison.OrdinalIgnoreCase);
            }
        }

        return user.Length > 1 ? Regex.Replace(text, $@"\b{Regex.Escape(user)}\b", "<user>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)) : text;
    }

    /// <summary>The new-issue link for <paramref name="entry"/> (already redacted). The title is the first exception line.</summary>
    public static string IssueUrl(string entry)
    {
        var lines = entry.Split('\n');
        var error = lines.Skip(1).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "Unknown error";
        var title = "Crash: " + (error.Length > 100 ? error[..100] + "…" : error);
        var head = $"https://github.com/{UpdateCheck.Repo}/issues/new?labels=crash&title={Uri.EscapeDataString(title)}&body=";

        static string Body(string log, bool cut) => new StringBuilder()
            .AppendLine("**What were you doing when it happened?**").AppendLine().AppendLine()
            .AppendLine("**From crash.log**").AppendLine("```text").AppendLine(log).AppendLine("```")
            .Append(cut ? "_Cut to fit the link; the rest is in crash.log._" : "").ToString();

        var url = head + Uri.EscapeDataString(Body(entry, false));
        for (var keep = entry.Length; url.Length > MaxUrl && keep > 200; keep = keep * 3 / 4)
        {
            url = head + Uri.EscapeDataString(Body(entry[..keep].TrimEnd() + "\n…", true));
        }

        return url;
    }

    /// <summary>The link for the newest crash on this machine, redacted; null when crash.log has no entry.</summary>
    public static string? ForLastCrash(string? folder = null) =>
        LastEntry(folder) is { } e ? IssueUrl(Redact(e, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName)) : null;

    // CrashLog.Write starts each entry with "yyyy-MM-dd HH:mm:ss +07:00 · " (older versions: without the offset)
    [GeneratedRegex(@"(?m)^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}( [+-]\d{2}:\d{2})? · ")]
    private static partial Regex EntryStart();
}
