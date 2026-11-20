// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;

using AiStackprint.Models;

namespace AiStackprint.Services;

/// <summary>state.json: what the app remembers on this machine (<see cref="UiState"/>), apart from your settings. Never exported.</summary>
public static class StateStore
{
    internal const string FileName = "state.json";
    private static readonly VersionedJson _format = new("state", 1);

    public static string File => Path.Combine(DataFolder.Root, FileName);

    public static UiState Load(string? file = null)
    {
        file ??= File;
        try
        {
            return _format.Read(file, Backups(file))?.Deserialize<UiState>(AppCatalog._opt) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public static void Save(UiState s, string? file = null)
    {
        try { _format.Write(file ?? File, (JsonObject)JsonSerializer.SerializeToNode(s, AppCatalog._opt)!); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // remembered for this run only
    }

    /// <summary>Values an older version kept in settings.json: taken over where state.json has none of its own.</summary>
    internal static void Adopt(JsonObject legacy, string file)
    {
        JsonObject state;
        try { state = _format.Read(file, Backups(file)) ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }

        var changed = false;
        foreach (var (k, v) in legacy)
        {
            if (!state.ContainsKey(k))
            {
                state[k] = v?.DeepClone();
                changed = true;
            }
        }

        if (changed)
        {
            try { _format.Write(file, state); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string Backups(string file) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "backups");
}
