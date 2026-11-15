// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>The right side of the panel: Content (the open file) and Files (everything in the folder), with Back for followed links.</summary>
internal partial class DetailPanel
{
    private DockPanel Content()
    {
        var tabs = Pills([("Content", "Content"), ("Files", "Files")], _tab, v => { _tab = v; Render(); });
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, Space.L) };
        DockPanel.SetDock(tabs, Dock.Right);
        bar.Children.Add(tabs);
        var crumbs = new List<Control>();
        if (_back.Count > 0 && _tab == "Content")
        {
            crumbs.Add(Btn("Back", GoBack, false, "arrow_left").With(b => ToolTip.SetTip(b, "Back to " + Rel(_back.Peek() ?? MainFile))));
        }

        if (_tab == "Content" && Current is { } cur)
        {
            crumbs.Add(Txt(Rel(cur), TextSize.Small, false, Muted).With(t => t.FontFamily = Theme.Mono));
            if (FileView.IsMarkdown(cur))
            {
                crumbs.Add(Btn(_raw ? "Rendered" : "Source", () => { _raw = !_raw; Render(); }, false, _raw ? "document_text" : "code"));
            }

            crumbs.Add(IconBtn("open", "Open in its app", () => PathUtil.OpenFile(cur)));
        }
        bar.Children.Add(Row([.. crumbs]));

        Control body;
        var scrolls = true;
        if (_tab == "Files")
        {
            body = FileList();
        }
        else if (Current is not { } file)
        {
            body = Txt("Nothing to show for this item.", TextSize.Small, false, Muted);
        }
        else
        {
            body = FileView.Open(file, _raw, Link);
            scrolls = false;   // the viewer scrolls by itself
        }

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(scrolls ? new ScrollViewer { Content = body, Padding = new Thickness(0, 0, Space.L, 0) } : body);
        return dock;
    }

    private string Rel(string? path) => path == null ? "" : Path.GetRelativePath(Root, path);

    private Control FileList()
    {
        var files = SkillDoc.Files(Root);
        if (files.Count == 0)
        {
            return Txt("No files in this folder.", TextSize.Small, false, Muted);
        }

        var rows = files.Select(f =>
        {
            var full = Path.Combine(Root, f.Rel);
            return new TableRow([Mono(f.Rel, TextSize.Small), Txt(LogViewer.Size(f.Size), TextSize.Small, false, Muted)], () => Open(full), full == Current);
        });
        return Table.Build([new Col("File", "*"), new Col("Size", "90", true)], rows);
    }

    private void Open(string file)
    {
        if (file != Current)
        {
            _back.Push(_file);
        }

        _file = file == MainFile ? null : file;
        _tab = "Content"; _raw = false;
        Render();
    }

    private void GoBack()
    {
        if (_back.Count == 0)
        {
            return;
        }

        _file = _back.Pop();
        _raw = false;
        Render();
    }

    /// <summary>
    /// A link in the open document. Web links open in the browser; files inside the folder open here; a folder shows Files.
    /// A link that leaves the folder is not followed here: it asks, then opens that place in the file manager.
    /// </summary>
    private async void Link(string link)
    {
        if (SkillDoc.IsExternal(link)) { PathUtil.OpenFile(link); return; }
        if (link.StartsWith('#') || Current is not { } from)
        {
            return;
        }

        if (SkillDoc.Resolve(Root, from, link) is { } inside)
        {
            if (Directory.Exists(inside)) { _tab = "Files"; Render(); }
            else
            {
                Open(inside);
            }

            return;
        }
        string outside;
        try { outside = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(from) ?? Root, Uri.UnescapeDataString(link.Split('#')[0]))); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { Toast.Show("Not a valid link: " + link, "warning"); return; }
        if (await Dialogs.Confirm(x.Owner, "Link outside this folder", $"\"{link}\" points outside {Path.GetFileName(Root)}:\n{outside}\n\nOpen that location in the file manager?", "Open location"))
        {
            PathUtil.OpenFolder(File.Exists(outside) || Directory.Exists(outside) ? outside : Path.GetDirectoryName(outside) ?? outside);
        }
    }
}
