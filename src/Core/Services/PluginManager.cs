using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>Installs and removes plugins through the app's own CLI (PluginCommands in apps.json). No shell is involved.</summary>
public static partial class PluginManager
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]*(@[A-Za-z0-9][A-Za-z0-9._\-/]*)?$")]
    private static partial Regex PluginId();

    static readonly string[] Scopes = { "user", "project", "local" };

    /// <summary>Fills {plugin} and {scope}. Rejects ids and scopes outside a strict whitelist so nothing can inject arguments.</summary>
    public static List<string> Build(IReadOnlyList<string> template, string plugin, string scope)
    {
        if (template.Count == 0) throw new InvalidOperationException("This app has no plugin command configured.");
        if (!PluginId().IsMatch(plugin)) throw new ArgumentException($"\"{plugin}\" is not a valid plugin id (name or name@marketplace).");
        if (!Scopes.Contains(scope)) throw new ArgumentException($"Unknown scope \"{scope}\".");
        return template.Select(a => a.Replace("{plugin}", plugin).Replace("{scope}", scope)).ToList();
    }

    public static Task<(int Code, string Output)> Install(AppDefinition d, string plugin, string scope, string? project) =>
        Run(Build(d.PluginCommands?.Install ?? new(), plugin, scope), project);

    public static Task<(int Code, string Output)> Remove(AppDefinition d, Item plugin) =>
        Run(Build(d.PluginCommands?.Remove ?? new(), plugin.Name, plugin.Scope ?? "user"), plugin.Project);

    /// <summary>Runs argv[0] found on PATH with the rest as separate arguments. cwd is the project folder for project scopes.</summary>
    public static async Task<(int Code, string Output)> Run(List<string> argv, string? cwd)
    {
        var exe = Which(argv[0]) ?? throw new FileNotFoundException($"\"{argv[0]}\" was not found on PATH.");
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in argv.Skip(1)) psi.ArgumentList.Add(a);
        if (cwd != null) psi.WorkingDirectory = cwd;
        using var p = Process.Start(psi)!;
        var output = new StringBuilder();
        var err = p.StandardError.ReadToEndAsync();
        output.Append(await p.StandardOutput.ReadToEndAsync());
        output.Append(await err);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { p.Kill(true); return (-1, output + "\nTimed out after 5 minutes."); }
        return (p.ExitCode, output.ToString().Trim());
    }

    /// <summary>Resolves a command on PATH, honouring PATHEXT on Windows (claude.exe, claude.cmd).</summary>
    public static string? Which(string cmd)
    {
        if (Path.IsPathRooted(cmd)) return File.Exists(cmd) ? cmd : null;
        var exts = OperatingSystem.IsWindows() && !Path.HasExtension(cmd)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var e in exts)
            {
                var f = Path.Combine(dir.Trim('"'), cmd + e.ToLowerInvariant());
                if (File.Exists(f)) return f;
            }
        return null;
    }
}
