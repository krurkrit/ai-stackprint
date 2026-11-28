// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <param name="UsedPercent">0–100.</param>
public record LimitWindow(string Name, double UsedPercent, DateTime? ResetsAt);

/// <param name="Plan">The plan the provider or the local sign-in names (Pro, Max 20x, Plus…).</param>
public record LimitSnapshot(string? Plan, List<LimitWindow> Windows, DateTime FetchedAt, string? Note = null, bool FromLogs = false);

public enum LimitState { Off, Ok, NoSignIn, Expired, SignInAgain, RateLimited, Offline, Error }

/// <param name="Previous">The reading before <see cref="Data"/>, for the pace forecast.</param>
public record LimitStatus(LimitState State, LimitSnapshot? Data, LimitSnapshot? Previous, string? Message, bool Busy);

/// <summary>A saved sign-in found on this computer. <see cref="File"/> is watched: a new sign-in there clears "sign in again".</summary>
public record Credential(string Token, DateTime? Expires, string File, string? Plan, IReadOnlyDictionary<string, string>? Extra = null);

/// <summary>Reads one app's saved sign-in and asks its provider for limits. To support a new app: add a class and list it in <see cref="LimitProviders.All"/>.</summary>
public interface ILimitProvider
{
    string Id { get; }
    /// <summary>The only host this provider calls.</summary>
    string Host { get; }
    Credential? Credential(string home);
    Task<LimitSnapshot> Fetch(HttpClient http, Credential c, CancellationToken ct);
    /// <summary>Limits the app wrote in its own logs (no network); null when it writes none.</summary>
    LimitSnapshot? FromLogs(AppDefinition d, IReadOnlyList<string> projects) => null;
    /// <summary>Every reading the app wrote in its own logs, for the pace chart (no network); empty when it writes none.</summary>
    IReadOnlyList<LimitReading> HistoryFromLogs(AppDefinition d, IReadOnlyList<string> projects) => [];
}

/// <summary>An HTTP reply that is not a success; 401/403 mean the saved sign-in is no longer accepted.</summary>
public class LimitHttpException(HttpStatusCode code, TimeSpan? retryAfter) : HttpRequestException($"The provider answered {(int)code} {code}.", null, code)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Live plan and limits for apps that have a provider. At most one call per app per refresh interval (one minute at least), one call at a time per app;
/// between calls the last reading is shown. Network calls only for apps the user turned on in Settings. Tokens stay in memory and are never logged.
/// Never refreshes a token or writes to a credential file: an expired token waits for the app itself to sign in again.
/// </summary>
public sealed class LimitsService(Func<Settings> settings, LimitHistory? history = null)
{
    /// <summary>Every reading kept for the pace chart (<see cref="LimitPacing"/>).</summary>
    public LimitHistory History { get; } = history ?? new LimitHistory();

    /// <summary>The HTTP client this feature uses. Tests give it one that answers from a function, so no request leaves the machine.</summary>
    internal static HttpClient Http { get; set; } = new() { Timeout = TimeSpan.FromSeconds(10) };
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);

    private sealed class Entry { public LimitStatus Status = new(LimitState.Off, null, null, null, false); public DateTime Fetched, RetryAfter, CredStamp; public bool Running, LogHistory; }
    private readonly Dictionary<string, Entry> _cache = [];
    private readonly Lock _lock = new();

    /// <summary>Raised (on a worker thread) with the app id when its status changes.</summary>
    public event Action<string>? Updated;

    public TimeSpan Interval => TimeSpan.FromMinutes(Math.Clamp(settings().LimitsRefreshMinutes, 1, 60));

    public bool Enabled(string appId) => settings().LimitsEnabled.Contains(appId);

    public LimitStatus Status(string appId)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(appId, out var e) ? e.Status : new(LimitState.Off, null, null, null, false);
        }
    }

    /// <summary>Tests: sets an app's status as if a refresh had just returned it (no call is due for a minute), and raises <see cref="Updated"/>.</summary>
    internal void Seed(string appId, LimitStatus status)
    {
        lock (_lock)
        {
            _cache[appId] = new Entry { Status = status, Fetched = DateTime.UtcNow };
        }

        Updated?.Invoke(appId);
    }

    /// <summary>When the last reading was taken; null before the first.</summary>
    public DateTime? Fetched(string appId)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(appId, out var e) && e.Fetched != default ? e.Fetched : null;
        }
    }

    /// <summary>Starts a refresh when one is due (or <paramref name="force"/>d, still at most once a minute). Returns at once; listen to <see cref="Updated"/>.</summary>
    public void Request(AppDefinition d, IReadOnlyList<string> projects, bool force)
    {
        if (LimitProviders.Find(d.Limits) is not { } p)
        {
            return;
        }

        Entry e;
        lock (_lock)
        {
            if (!_cache.TryGetValue(d.Id, out e!))
            {
                _cache[d.Id] = e = new Entry();
            }

            var age = DateTime.UtcNow - e.Fetched;
            if (e.Running || DateTime.UtcNow < e.RetryAfter || age < MinInterval || (!force && age < Interval))
            {
                return;
            }

            e.Running = true;
            e.Status = e.Status with { Busy = true };
        }
        Updated?.Invoke(d.Id);
        _ = Task.Run(() => Refresh(p, d, [.. projects], e));
    }

    public void Request(AppDefinition d, IReadOnlyList<string> projects) => Request(d, projects, false);

    private async Task Refresh(ILimitProvider p, AppDefinition d, List<string> projects, Entry e)
    {
        var old = e.Status.Data;
        LimitStatus next;
        DateTime retry = default;
        LimitSnapshot? logs = null;
        try
        {
            logs = p.FromLogs(d, projects);
            if (!e.LogHistory)
            {
                e.LogHistory = true;   // the app's older readings, once a run; new ones come with each refresh
                History.Add(d.Id, p.HistoryFromLogs(d, projects), DateTime.Now);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or System.Text.Json.JsonException) { }   // no reading from the log
        try
        {
            if (!Enabled(d.Id))
            {
                next = new(logs != null ? LimitState.Ok : LimitState.Off, logs, old, logs == null ? "Live limits are off for this app." : null, false);
            }
            else if (p.Credential(PathUtil.Home) is not { } c)
            {
                next = new(LimitState.NoSignIn, logs, old, "No saved sign-in was found in a file (it may be in the system keychain).", false);
            }
            else if (e.Status.State == LimitState.SignInAgain && Stamp(c.File) == e.CredStamp)
            {
                next = e.Status with { Busy = false };   // wait for a new sign-in
            }
            else if (c.Expires is { } exp && exp < DateTime.UtcNow)
            {
                next = new(LimitState.Expired, logs ?? old, old, ExpiredMessage(d.Name, p.Id switch { "claude" => "claude", "codex" => "codex", "gemini" => "gemini", _ => null }, exp), false);
            }
            else if (logs != null && DateTime.UtcNow - logs.FetchedAt.ToUniversalTime() < TimeSpan.FromMinutes(10))
            {
                next = new(LimitState.Ok, logs, old, null, false);   // the app's own log is fresh: no call
            }
            else
            {
                e.CredStamp = Stamp(c.File);
                var snap = await p.Fetch(Http, c, CancellationToken.None).ConfigureAwait(false);
                next = new(LimitState.Ok, snap with { Plan = snap.Plan ?? c.Plan }, old, null, false);
            }
        }
        catch (LimitHttpException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        { next = new(LimitState.SignInAgain, old ?? logs, old, $"{d.Name} no longer accepts the saved sign-in. Sign in again in {d.Name}.", false); }
        catch (LimitHttpException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var wait = ex.RetryAfter ?? TimeSpan.FromMinutes(5);
            retry = DateTime.UtcNow + (wait > TimeSpan.FromMinutes(15) ? TimeSpan.FromMinutes(15) : wait);
            next = new(LimitState.RateLimited, old ?? logs, old, $"The provider asked to wait; trying again after {retry.ToLocalTime():HH:mm}.", false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException
                                       or ArgumentException or FormatException or OverflowException)   // a reply or file with values out of range
        { next = new(ex is HttpRequestException or TaskCanceledException ? LimitState.Offline : LimitState.Error, old ?? logs, old, ShortMessage(ex), false); }
        finally
        {
            // Whatever happened, the app is not left "busy" forever: the next refresh can run
            lock (_lock)
            {
                if (e.Running)
                {
                    e.Running = false;
                    e.Fetched = DateTime.UtcNow;
                    e.Status = e.Status with { Busy = false };
                }
            }
        }
        lock (_lock)
        {
            e.Status = next;
            e.RetryAfter = retry;
        }
        if (next.State == LimitState.Ok && next.Data is { Windows.Count: > 0 } data)
        {
            History.Add(d.Id, new LimitReading(data.FetchedAt, data.Windows), DateTime.Now);
        }

        Updated?.Invoke(d.Id);
    }

    /// <summary>Says when the saved sign-in ran out and how to renew it (the app never refreshes it itself).</summary>
    public static string ExpiredMessage(string app, string? cli, DateTime expiresUtc)
    {
        var age = DateTime.UtcNow - expiresUtc;
        var ago = age.TotalHours < 1 ? "less than an hour ago" : age.TotalHours < 48 ? $"{(int)age.TotalHours} hours ago" : $"{(int)age.TotalDays} days ago";
        var renew = cli != null ? $"Run `{cli}` once to renew." : $"Open {app} once to renew.";
        return $"Sign-in expired {ago}. {renew}";
    }

    internal static string ShortMessage(Exception ex) => ex switch
    {
        TaskCanceledException => "The provider did not answer in time.",
        HttpRequestException h when h.StatusCode != null => $"The provider answered {(int)h.StatusCode}.",
        HttpRequestException => "Cannot reach the provider (offline?).",
        _ => "Could not read the reply: " + ex.Message
    };

    private static DateTime Stamp(string file) { try { return File.GetLastWriteTimeUtc(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return default; } }

    /// <summary>When a window reaches 100 % at the pace between two readings; null when not rising or it resets first.</summary>
    public static DateTime? FullAt(LimitWindow before, DateTime beforeAt, LimitWindow now, DateTime nowAt)
    {
        double rise = now.UsedPercent - before.UsedPercent, hours = (nowAt - beforeAt).TotalHours;
        if (rise <= 0 || hours <= 0 || now.UsedPercent >= 100)
        {
            return null;
        }

        var full = nowAt.AddHours((100 - now.UsedPercent) * hours / rise);
        return now.ResetsAt is { } r && full >= r ? null : full;
    }
}
