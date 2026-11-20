// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

internal partial class DetailPanel
{
    /// <summary>Front matter key as a label: "metadata.version" → "Version", "allowed-tools" → "Allowed tools".</summary>
    private static string Label(string key)
    {
        var k = key[(key.LastIndexOf('.') + 1)..].Replace('-', ' ').Replace('_', ' ');
        return k.Length == 0 ? key : char.ToUpperInvariant(k[0]) + k[1..];
    }

    /// <summary>
    /// What a plugin brings (<see cref="PluginParts"/>: its manifest or its folders), with counts where its folder has them.
    /// Hooks, MCP and LSP servers run programs on this computer, so they are flagged.
    /// </summary>
    private Control Contents()
    {
        int Count(string dir, string pattern)
        {
            var p = Path.Combine(Root, dir);
            return Directory.Exists(p) ? Directory.EnumerateFiles(p, pattern, SearchOption.AllDirectories).Count() : 0;
        }
        bool Has(string key) => It.Provides.Contains(key);
        var skills = x.S.Items.Count(i => i.Kind == "Skill" && i.Plugin == It.Name && i.App == It.App);
        var parts = new WrapPanel
        {
            ItemSpacing = Space.S,
            LineSpacing = Space.S
        };
        foreach (var (key, label, n) in new[] { ("skills", "skills", skills), ("commands", "commands", Count("commands", "*.md")), ("agents", "agents", Count("agents", "*.md")) })
        {
            if (n > 0 || Has(key))
            {
                parts.Children.Add(Chip(n > 0 ? $"{n} {label}" : PluginParts.Name(key)));   // named in the manifest at another place: no count
            }
        }

        foreach (var (key, label, tip) in new[]
        {
            ("hooks", "Hooks · runs commands", "Hooks run commands on this computer when the app does certain things."),
            ("mcp", "MCP servers · runs programs", "MCP servers are programs the app starts on this computer."),
            ("lsp", "LSP servers · runs programs", "Language servers are programs the app starts on this computer for code intelligence.")
        })
        {
            if (Has(key))
            {
                parts.Children.Add(Chip(label, WarningKey).With(c => ToolTip.SetTip(c, tip)));
            }
        }

        return parts.Children.Count == 0 ? new Panel() : new StackPanel
        {
            Spacing = Space.S,
            Children = { Txt("Contains", TextSize.Small, false, Muted), parts }
        };
    }

    private static StackPanel Findings(List<Finding> f)
    {
        var sp = new StackPanel
        {
            Spacing = Space.S,
            Children = { Txt("Checks", TextSize.Small, false, Muted) }
        };
        foreach (var x in f)
        {
            sp.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = Space.M,
                Children =
            {
                Icon(x.Warning ? "warning" : "info", IconSize.S).Themed(TextElement.ForegroundProperty, x.Warning ? WarningKey : Muted).With(i => i.VerticalAlignment = VerticalAlignment.Top),
                Txt(x.Text, TextSize.Small).With(t => Grid.SetColumn(t, 1))
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
            Child = AppName(d?.Logo, d?.Category ?? "custom", d?.Name ?? fallback, Sizes.LogoS)
        };
        ToolTip.SetTip(b, "Open the app page");
        b.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left)
            {
                e.Handled = true;
                x.Go("App:" + appId);
            }
        };
        return b;
    }
}
