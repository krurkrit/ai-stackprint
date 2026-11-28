// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace AiStackprint.Ui;

/// <summary>
/// One copy of the app per user. The first copy holds a lock (Windows: the mutex <c>Local\AiStackprint</c>; elsewhere <c>~/.ai-stackprint/app.lock</c>)
/// and listens on a pipe (a Unix socket outside Windows) that only this user can open. A second start sends its message there
/// (<c>show</c>, <c>show &lt;page&gt;</c>, <c>pause</c>) and exits.
/// </summary>
internal static class SingleInstance
{
    private const int ConnectMs = 2000, MaxMessage = 512;
#pragma warning disable S1450, S4487, S2930 // held, never read or disposed, for the life of the process: the system lets go of the lock when it ends
    private static Mutex? _mutex;
    private static FileStream? _lock;
#pragma warning restore S1450, S4487, S2930

    /// <summary>The pipe of this user: a short hash of the user and machine names (the pipe name is visible to other users, the names are not).</summary>
    public static string PipeName { get; } = "AiStackprint-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];

    /// <summary>True when this is the first copy. Otherwise the message goes to the first copy and the caller exits.</summary>
    public static bool Claim(string message)
    {
        if (TryLock(Path.Combine(Services.DataFolder.Root, "app.lock")))
        {
            return true;
        }

        Send(PipeName, message);
        return false;
    }

    internal static bool TryLock(string lockFile)
    {
        if (OperatingSystem.IsWindows())
        {
            _mutex = new Mutex(true, @"Local\AiStackprint", out var created);
            if (!created)
            {
                _mutex.Dispose();
                _mutex = null;
            }

            return created;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lockFile)!);
#pragma warning disable S2930 // held for the life of the process
            _lock = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);   // the system lets go of it when the process ends
#pragma warning restore S2930
            return true;
        }
        catch (IOException) { return false; }   // another copy holds it
        catch (UnauthorizedAccessException) { return true; }   // cannot lock at all: run, as before this check existed
    }

    /// <summary>Sends one line to the first copy; false when it does not answer within two seconds.</summary>
    internal static bool Send(string pipe, string message)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            c.Connect(ConnectMs);
            var bytes = Encoding.UTF8.GetBytes(message.Length > MaxMessage ? message[..MaxMessage] : message);
            c.Write(bytes);   // no Flush: on a pipe it waits until the other side has read
            return true;
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Listens until <paramref name="ct"/> is cancelled; each message is handed to <paramref name="received"/> (on a worker thread).</summary>
    internal static async Task Listen(string pipe, Action<string> received, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var s = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await s.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var buf = new byte[MaxMessage];
                var n = 0;
                int read;
                while (n < buf.Length && (read = await s.ReadAsync(buf.AsMemory(n), ct).ConfigureAwait(false)) > 0)
                {
                    n += read;
                }

                if (n > 0)
                {
                    received(Encoding.UTF8.GetString(buf, 0, n).Trim());
                }
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { await Task.Delay(ConnectMs / 4, CancellationToken.None).ConfigureAwait(false); }   // a broken connection: listen again
        }
    }

    /// <summary>
    /// The message for the first copy from this start's arguments: <c>--open &lt;page&gt;</c> or a notification's link (<c>aistackprint:open/App:claude-code</c>,
    /// <c>aistackprint:pause</c>); <c>show</c> otherwise.
    /// </summary>
    public static string Message(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--open" && i + 1 < args.Count)
            {
                return "show " + args[i + 1];
            }

            if (args[i].StartsWith(Protocol + ":", StringComparison.OrdinalIgnoreCase))
            {
                var what = Uri.UnescapeDataString(args[i][(Protocol.Length + 1)..]).Trim('/');
                return what == "pause" ? "pause" : what.StartsWith("open/", StringComparison.Ordinal) ? "show " + what[5..] : "show";
            }
        }

        return "show";
    }

    /// <summary>The link scheme notification buttons open (registered for this user while alerts can be sent; see <see cref="Notifier"/>).</summary>
    public const string Protocol = "aistackprint";
}
