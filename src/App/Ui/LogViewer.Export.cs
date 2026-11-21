// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Platform.Storage;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Export: the open session written to a Markdown or HTML file the user names in a save dialog. The log itself is never changed.</summary>
internal partial class LogViewer
{
    private static readonly System.Text.UTF8Encoding _utf8 = new(false);

    /// <summary>The toolbar button; null unless a session (not a text log) is shown as a conversation.</summary>
    private Avalonia.Controls.Button? ExportButton() =>
        ShowReadable && !IsLines ? IconBtn("save", "Export this session as Markdown or HTML", () => Fire.Forget(ExportAsync())) : null;

    private async Task ExportAsync()
    {
        if (_readable?.Session is not { } session || _file == null)
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(_file.Path);
        var picked = await _x.Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export session",
            SuggestedFileName = name + ".md",
            DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }, new FilePickerFileType("HTML") { Patterns = ["*.html"] }]
        });
        if (picked?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            var html = path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(path, html ? SessionExport.Html(session, name) : SessionExport.Markdown(session, name), _utf8, CancellationToken.None);
            Toast.Show("Exported to " + path, "save", 4);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Toast.Show("Could not write the export: " + e.Message, "warning", 8); }
    }
}
