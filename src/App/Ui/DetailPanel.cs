// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
    private readonly ContentControl _host = new();
    private Action _close = () => { };

    public Control Build(Item item, Action close)
    {
        if (_item == null || !ItemBrowser.Same(_item, item)) { _file = null; _back.Clear(); _tab = "Content"; _raw = false; }   // a rescan keeps the open file
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
        var meta = new ScrollViewer { Content = Meta(), Padding = new Thickness(0, 0, 14, 0) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*"), ColumnSpacing = 18 };
        var content = Content();
        Grid.SetColumn(content, 1);
        grid.Children.Add(meta); grid.Children.Add(content);
        _host.Content = new DockPanel { Margin = new Thickness(32, 12, 32, 16), Children = { head, grid } };
    }

    private Control Header()
    {
        var d = x.S.Def(It.App);
        var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { Chip(It.Kind), Chip(AppDetailView.LevelLabel(It), LevelKey(It.Level)) } };
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
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(actions, Dock.Right);
        dock.Children.Add(actions);
        dock.Children.Add(Row(AppLogo(d?.Logo, d?.Category ?? "custom", 32), Sel(It.Name, 18, true).With(t => t.VerticalAlignment = VerticalAlignment.Center), chips));
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
    private Control Meta()
    {
        var sp = new StackPanel { Spacing = 12 };
        SkillDoc? doc = null;
        if (It.Kind == "Skill")
        {
            try { doc = SkillDoc.Read(It.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { sp.Children.Add(Txt("Cannot read: " + e.Message, 13, false, Muted)); }
        }

        if (It.Description != "")
        {
            sp.Children.Add(Sel(It.Description, 14));
        }

        var kv = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*"), RowSpacing = 6, ColumnSpacing = 10 };
        void Add(string k, Control v)
        {
            kv.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var r = kv.RowDefinitions.Count - 1;
            var a = Txt(k, 13, false, Muted);
            Grid.SetRow(a, r); Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            kv.Children.Add(a); kv.Children.Add(v);
        }
        void AddText(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) { Add(k, Sel(v, 13)); } }
        Add("App", AppLink(x, It.App, It.Source));
        if (It.LevelLabel != null)
        {
            AddText("App's term", It.LevelLabel);
        }

        if (It.Kind == "Skill") { Add("Invoked by", AppDetailView.InvokeCell(It)); AddText("From", AppDetailView.From(It)); }
        AddText("Plugin", It.Plugin);
        AddText("Scope", It.Kind == "Plugin" ? It.Scope : null);
        AddText("Author", It.Author);
        if (doc != null)
        {
            foreach (var (k, v) in doc.Front.Where(f => f.Value != "" && f.Key is not ("name" or "description" or "author" or "metadata.author")))
            {
                AddText(Label(k), v);
            }
        }

        Add("Path", Sel(It.Path, 12).With(t => t.FontFamily = Theme.Mono));
        if (doc != null)
        {
            var chars = doc.Body.Length;
            AddText("Size", $"{chars:N0} characters · ≈{chars / 4:N0} tokens · {doc.Lines:N0} lines");
            AddText("In context", "Name and description always; the body when it is used");
        }
        sp.Children.Add(kv);

        if (It.Kind == "Plugin")
        {
            sp.Children.Add(PluginParts());
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

    /// <summary>Front matter key as a label: "metadata.version" → "Version", "allowed-tools" → "Allowed tools".</summary>
    private static string Label(string key)
    {
        var k = key[(key.LastIndexOf('.') + 1)..].Replace('-', ' ').Replace('_', ' ');
        return k.Length == 0 ? key : char.ToUpperInvariant(k[0]) + k[1..];
    }

    /// <summary>What a plugin brings, from its folder. Hooks and MCP servers run programs on this computer, so they are flagged.</summary>
    private Control PluginParts()
    {
        int Count(string dir, string pattern) { var p = Path.Combine(Root, dir); return Directory.Exists(p) ? Directory.EnumerateFiles(p, pattern, SearchOption.AllDirectories).Count() : 0; }
        var skills = x.S.Items.Count(i => i.Kind == "Skill" && i.Plugin == It.Name && i.App == It.App);
        bool hooks = File.Exists(Path.Combine(Root, "hooks", "hooks.json")), mcp = File.Exists(Path.Combine(Root, ".mcp.json"));
        var parts = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (label, n) in new[] { ("skills", skills), ("commands", Count("commands", "*.md")), ("agents", Count("agents", "*.md")) })
        {
            if (n > 0)
            {
                parts.Children.Add(Chip($"{n} {label}"));
            }
        }

        if (hooks)
        {
            parts.Children.Add(Chip("Hooks · runs commands", WarningKey).With(c => ToolTip.SetTip(c, "Hooks run commands on this computer when the app does certain things.")));
        }

        if (mcp)
        {
            parts.Children.Add(Chip("MCP servers · runs programs", WarningKey).With(c => ToolTip.SetTip(c, "MCP servers are programs the app starts on this computer.")));
        }

        return parts.Children.Count == 0 ? new Panel() : new StackPanel { Spacing = 6, Children = { Txt("Contains", 13, false, Muted), parts } };
    }

    private static Control Findings(List<Finding> f)
    {
        var sp = new StackPanel { Spacing = 6, Children = { Txt("Checks", 13, false, Muted) } };
        foreach (var x in f)
        {
            sp.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 8,
                Children =
            {
                Icon(x.Warning ? "warning" : "info", 14).Themed(TextElement.ForegroundProperty, x.Warning ? WarningKey : Muted).With(i => i.VerticalAlignment = VerticalAlignment.Top),
                Txt(x.Text, 13).With(t => Grid.SetColumn(t, 1))
            }
            });
        }

        return sp;
    }

    /// <summary>App logo and name that opens the app page.</summary>
    public static Control AppLink(Ctx x, string appId, string fallback)
    {
        var d = x.S.Def(appId);
        var b = new Border
        {
            Background = Avalonia.Media.Brushes.Transparent,
            Cursor = Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = AppName(d?.Logo, d?.Category ?? "custom", d?.Name ?? fallback, 22)
        };
        ToolTip.SetTip(b, "Open the app page");
        b.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left) { e.Handled = true; x.Go("App:" + appId); } };
        return b;
    }
}
