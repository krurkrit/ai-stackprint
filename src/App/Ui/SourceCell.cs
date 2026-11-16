// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Input;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Where a server or hook is written: "from plugin X" (a link that opens the plugin) above the file's path, or the path alone.</summary>
internal static class SourceCell
{
    public static Control For(Ctx x, string? plugin, string app, string file)
    {
        var path = ItemBrowser.PathCell(file);
        if (plugin == null)
        {
            return path;
        }

        var link = Txt("from plugin " + plugin.Split('@')[0], TextSize.Small, true, Accent);
        if (x.S.Items.FirstOrDefault(i => i.Kind == "Plugin" && i.Name == plugin && i.App == app) is { } item)
        {
            link.Cursor = Hand;
            ToolTip.SetTip(link, "Open the plugin");
            link.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) { e.Handled = true; x.Open(item); } };
        }

        return new StackPanel { Spacing = Space.XXS, Children = { link, path } };
    }
}
