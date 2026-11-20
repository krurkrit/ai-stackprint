// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Bottom panel for one skill or plugin: everything known about it on the left, its files on the right.
/// SKILL.md (or a plugin's README) is rendered; links to files inside the folder open here, with Back; links that leave the folder ask first.
/// </summary>
internal partial class DetailPanel(Ctx x)
{
    private Item? _item;
    private string? _file;                                 // file shown; null = the item's main file
    private readonly Stack<string?> _back = new();
    private string _tab = "Content";                       // Content | Files
    private bool _raw;
    private ContentControl _host = new();
    private Action _close = () => { };

    /// <summary>The page was left: a new host and no callback into the old page, so nothing here keeps that page in memory.</summary>
    public void Leave()
    {
        _host = new();
        _close = () => { };
    }

    public Control Build(Item item, Action close)
    {
        if (_item == null || !ItemBrowser.Same(_item, item))
        {
            _file = null;
            _back.Clear();
            _tab = "Content";
            _raw = false;
        }   // a rescan keeps the open file
        _item = item;
        _close = close;
        Render();
        return _host;
    }

    private Item It => _item!;
    private string Root => It.Kind == "Plugin" ? It.Path : Path.GetDirectoryName(It.Path) ?? It.Path;

    /// <summary>Skill: its .md. Plugin: README.md, else its manifest.</summary>
    private string? MainFile => It.Kind != "Plugin" ? It.Path
        : new[] { "README.md", "readme.md", Path.Combine(".claude-plugin", "plugin.json") }.Select(f => Path.Combine(Root, f)).FirstOrDefault(File.Exists);

    private string? Current => _file ?? MainFile;

    private void Render()
    {
        var head = Header();
        DockPanel.SetDock(head, Dock.Top);
        var meta = new ScrollViewer
        {
            Content = Meta(),
            Padding = new Thickness(0, 0, Space.XL, 0)
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Widths.SidePanel},*"),
            ColumnSpacing = Space.XXL
        };
        var content = Content();
        Grid.SetColumn(content, 1);
        grid.Children.Add(meta);
        grid.Children.Add(content);
        _host.Content = new DockPanel
        {
            Margin = new Thickness(Space.Page, Space.L, Space.Page, Space.XL),
            Children = { head, grid }
        };
    }

    private DockPanel Header()
    {
        var d = x.S.Def(It.App);
        var chips = new WrapPanel
        {
            ItemSpacing = Space.S,
            LineSpacing = Space.S,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Chip(It.Kind), Chip(AppDetailView.LevelLabel(It), LevelKey(It.Level)) }
        };
        if (It.Version != null)
        {
            chips.Children.Add(Chip(It.Version));
        }

        var actions = Row(IconBtn("folder_open", "Open folder", () => PathUtil.OpenFolder(It.Path)), IconBtn("copy", "Copy path", () => Toast.Copy(x.Owner, It.Path)));
        if (RemoveAction() is { } remove)
        {
            ((StackPanel)actions).Children.Add(IconBtn("delete", It.Kind == "Plugin" ? "Remove plugin" : "Remove skill", remove, ErrorKey));
        }

        ((StackPanel)actions).Children.Add(IconBtn("dismiss", "Close (Esc)", () => _close()));
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, Space.L) };
        DockPanel.SetDock(actions, Dock.Right);
        dock.Children.Add(actions);
        dock.Children.Add(Row(AppLogo(d?.Logo, d?.Category ?? "custom", Sizes.LogoL), Sel(It.Name, TextSize.Title, true).With(t => t.VerticalAlignment = VerticalAlignment.Center), chips));
        return dock;
    }

    private Action? RemoveAction()
    {
        if (It.Kind == "Skill" && SkillManager.CannotRemove(It) == null)
        {
            return () => AppActions.RemoveSkill(x, It);
        }

        if (It.Kind == "Plugin" && x.S.Def(It.App) is { PluginCommands: not null } d)
        {
            return () => AppActions.RemovePlugin(x, d, It);
        }

        return null;
    }

    /// <summary>Left column: description, where it is, who can start it, front matter, size, checks.</summary>
    private StackPanel Meta()
    {
        var sp = new StackPanel { Spacing = Space.L };
        SkillDoc? doc = null;
        if (It.Kind == "Skill")
        {
            try { doc = SkillDoc.Read(It.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { sp.Children.Add(Txt("Cannot read: " + e.Message, TextSize.Small, false, Muted)); }
        }

        if (It.Description != "")
        {
            sp.Children.Add(Sel(It.Description, TextSize.Body));
        }

        var kv = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Widths.LabelS},*"),
            RowSpacing = Space.S,
            ColumnSpacing = Space.L
        };
        void Add(string k, Control v)
        {
            kv.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var r = kv.RowDefinitions.Count - 1;
            var a = Txt(k, TextSize.Small, false, Muted);
            Grid.SetRow(a, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            kv.Children.Add(a);
            kv.Children.Add(v);
        }
        void AddText(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) { Add(k, Sel(v, TextSize.Small)); } }
        Add("App", AppLink(x, It.App, It.Source));
        if (It.LevelLabel != null)
        {
            AddText("App's term", It.LevelLabel);
        }

        if (It.Kind == "Skill")
        {
            Add("Invoked by", AppDetailView.InvokeCell(It));
            AddText("From", AppDetailView.From(It));
        }
        AddText("Plugin", It.Plugin);
        AddText("Scope", It.Kind == "Plugin" ? It.Scope : null);
        AddText("Author", It.Author);
        AddText("Keywords", It.Keywords.Count == 0 ? null : string.Join(", ", It.Keywords));
        if (doc != null)
        {
            foreach (var (k, v) in doc.Front.Where(f => f.Value != "" && f.Key is not ("name" or "description" or "author" or "metadata.author")))
            {
                AddText(Label(k), v);
            }
        }

        Add("Path", Sel(It.Path, TextSize.Caption).With(t => t.FontFamily = Theme.Mono));
        if (doc != null)
        {
            var chars = doc.Body.Length;
            AddText("Size", $"{chars:N0} characters · ≈{chars / 4:N0} tokens · {doc.Lines:N0} lines");
            AddText("In context", "Name and description always; the body when it is used");
        }
        sp.Children.Add(kv);

        if (It.Kind == "Plugin")
        {
            sp.Children.Add(Contents());
        }

        var dup = x.S.Items.Where(i => i.Kind == It.Kind && i.Name == It.Name && !ItemBrowser.Same(i, It)).Select(i => $"{i.Level} ({i.Source})").Distinct().ToList();
        var findings = doc != null ? SkillCheck.Check(It.Path, doc) : [];
        if (dup.Count > 0)
        {
            findings.Insert(0, new Finding("Same name also at: " + string.Join(", ", dup), false));
        }

        if (findings.Count > 0)
        {
            sp.Children.Add(Findings(findings));
        }

        return sp;
    }
}
