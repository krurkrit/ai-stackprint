// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Config file viewer: read-only with highlighting; Edit → Save (validate, conflict check, backup) or Cancel. Folders show their entries.</summary>
internal class ConfigPanel : StackPanel
{
    private readonly Ctx _x;
    private readonly string _path;
    private readonly CodeView? _code;
    private readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, Spacing = Space.M };
    private readonly TextBlock _status = Txt("", TextSize.Caption, false, Muted).With(t => { t.VerticalAlignment = VerticalAlignment.Center; t.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; });
    private ConfigEditor.Stamp? _stamp;
    private string _original = "";

    public ConfigPanel(Ctx x, string path)
    {
        _x = x; _path = path; Spacing = Space.M;
        if (Directory.Exists(path))
        {
            Children.Add(Txt("Folder contents", TextSize.Caption, false, Muted));
            Children.Add(Mono(Entries(path), TextSize.Caption));
            return;
        }
        _code = new CodeView(path) { Height = Heights.ViewerS };
        try { (_original, _stamp) = ConfigEditor.Read(path); _code.Text = _original; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = "Cannot read: " + e.Message; }
        _code.Changed += (_, _) => { if (!_code.ReadOnly) { _status.Text = _code.Text == _original ? "Editing" : "Unsaved changes"; } };
        var head = new DockPanel { Children = { _bar.With(b => DockPanel.SetDock(b, Dock.Right)), Row(Title(Path.GetFileName(path), TextSize.Body), _status) } };
        Children.Add(head);
        Children.Add(new Border { CornerRadius = new CornerRadius(Radius.M), ClipToBounds = true, Child = _code });
        ShowBar();
    }

    private static string Entries(string dir)
    {
        try { return string.Join("\n", Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).Order()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "Cannot read: " + e.Message; }
    }

    private void ShowBar()
    {
        _bar.Children.Clear();
        if (_code == null || _stamp == null)
        {
            return;
        }

        if (_code.ReadOnly)
        {
            if (ConfigEditor.CanEdit(_path))
            {
                _bar.Children.Add(Btn("Edit", Edit, false, "edit"));
            }
            else
            {
                _status.Text = "Read-only: larger than 2 MB";
            }
        }
        else
        {
            _bar.Children.Add(Btn("Cancel", Cancel));
            _bar.Children.Add(Btn("Save", Save, true, "save"));
        }
    }

    private void Edit() { _code!.ReadOnly = false; _status.Text = "Editing"; ShowBar(); }

    private void Cancel() { _code!.Text = _original; _code.ReadOnly = true; _status.Text = ""; ShowBar(); }

    private async void Save()
    {
        if (_code!.Text == _original) { Cancel(); return; }
        if (ConfigEditor.Validate(_path, _code.Text) is { } error) { await Dialogs.Message(_x.Owner, "Not saved", error); return; }
        if (!await Dialogs.Confirm(_x.Owner, $"Save {Path.GetFileName(_path)}?",
                $"Writes {_path}.\nThe current file is copied to {ConfigEditor.BackupRoot} first.", "Save"))
        {
            return;
        }

        try
        {
            var backup = ConfigEditor.Save(_path, _code.Text, _stamp!);
            (_original, _stamp) = ConfigEditor.Read(_path);
            _code.ReadOnly = true; _status.Text = "Saved · backup kept"; ToolTip.SetTip(_status, backup); ShowBar();
        }
        catch (FileChangedException e)
        {
            if (await Dialogs.Confirm(_x.Owner, "File changed on disk", e.Message + "\nReload it? Your edits are discarded.", "Reload"))
            { (_original, _stamp) = ConfigEditor.Read(_path); Cancel(); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.Message(_x.Owner, "Not saved", e.Message);
        }
    }
}
