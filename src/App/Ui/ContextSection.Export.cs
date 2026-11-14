// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

namespace AiStackprint.Ui;

internal partial class ContextSection
{
    /// <summary>Previews AGENTS.md built from the loaded files; on confirm creates it, or replaces it after a backup (refused if it changed since the preview opened).</summary>
    private async void Export(string project)
    {
        if (_last == null)
        {
            Toast.Show("Still reading the instruction files", "info");
            return;
        }

        var (sources, text) = AgentsExport.Build(project, _last.Select(r => r.Report), DateTime.Today);
        if (text == null)
        {
            Toast.Show("No instruction files here to export", "info");
            return;
        }

        var path = Path.Combine(project, "AGENTS.md");
        var exists = File.Exists(path);
        var stamp = exists ? ConfigEditor.StampOf(path) : null;
        var intro = $"From {ReadableLog.Plural(sources.Count, "file")}: {string.Join(", ", sources)}. " + (exists ? "AGENTS.md exists: it is copied to the backups folder, then replaced." : "AGENTS.md does not exist yet.");
        if (!await Dialogs.Preview(x.Owner, "Export to AGENTS.md", intro, text, exists ? "Replace AGENTS.md (backup first)" : "Save AGENTS.md"))
        {
            return;
        }

        try
        {
            if (exists)
            {
                ConfigEditor.Save(path, text, stamp!);
            }
            else
            {
                ConfigEditor.Create(path, text);
            }

            Toast.Show("AGENTS.md saved", "info");
            x.Rescan();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Toast.Show(e is FileChangedException ? "AGENTS.md changed after the preview opened: nothing was written. Try again." : "Could not write AGENTS.md: " + e.Message, "warning", 8);
        }
    }
}
