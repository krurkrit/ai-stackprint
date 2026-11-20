// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Input;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>A config file's "Holds" cell: "2 MCP servers · 1 hook · model claude-opus-5-5"; servers and hooks are links to their page.</summary>
internal static class HoldsCell
{
    public static Control For(Ctx x, Item config)
    {
        var parts = ConfigSummary.Of(config, x.S.Mcp, x.S.Hooks);
        var wrap = new WrapPanel();
        for (var n = 0; n < parts.Count; n++)
        {
            var p = parts[n];
            var t = Txt(p.Text + (n < parts.Count - 1 ? " · " : ""), TextSize.Small, false, p.Page != null ? Accent : Muted);
            if (p.Page is { } page)
            {
                t.Cursor = Hand;
                ToolTip.SetTip(t, page == "Mcp" ? "Open the Servers page" : "Open the Hooks page");
                t.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton == MouseButton.Left)
                    {
                        e.Handled = true;
                        x.Go(page);
                    }
                };
            }

            wrap.Children.Add(t);
        }

        return wrap;
    }
}
