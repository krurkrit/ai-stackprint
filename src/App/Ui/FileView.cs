// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Shows one file read-only: Markdown rendered (front matter left out), text highlighted, images as images.</summary>
internal static class FileView
{
    private const long MaxText = 2 << 20;
    private static readonly string[] _images = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico"];
    private static readonly string[] _markdown = [".md", ".markdown", ".mdc"];

    public static bool IsMarkdown(string path) => _markdown.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <param name="raw">Markdown as written (highlighted source) instead of rendered.</param>
    /// <param name="link">Called with a link's target when a rendered link is clicked.</param>
    /// <param name="scrolls">false when the result scrolls by itself (code), so the caller does not wrap it in a ScrollViewer.</param>
    public static Control Show(string path, bool raw, Action<string> link, out bool scrolls)
    {
        scrolls = true;
        if (!File.Exists(path))
        {
            return Txt("File not found: " + path, TextSize.Small, false, Muted);
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (_images.Contains(ext))
            {
                using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return new Image { Source = new Bitmap(s), MaxHeight = Heights.ViewerL, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Avalonia.Media.Stretch.Uniform };
            }
            var len = new FileInfo(path).Length;
            if (len > MaxText)
            {
                return TooBig(path, len);
            }

            if (IsMarkdown(path) && !raw)
            {
                return MarkdownView.Document(SkillDoc.Read(path).Body, link);
            }

            var text = Read(path);
            if (text.Contains('\0'))
            {
                return TooBig(path, len, binary: true);
            }

            scrolls = false;
            return new Border { CornerRadius = new Avalonia.CornerRadius(6), ClipToBounds = true, BorderThickness = new Avalonia.Thickness(1), Child = new CodeView(path) { Text = text } }
                .Themed(Border.BorderBrushProperty, BorderKey);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Txt("Cannot show this file: " + e.Message, TextSize.Small, false, Muted);
        }
    }

    /// <summary>Text of a file another app may be writing.</summary>
    public static string Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs);
        return r.ReadToEnd();
    }

    private static StackPanel TooBig(string path, long len, bool binary = false) => new()
    {
        Spacing = Space.L,
        Children = { Txt(binary ? "This file is not text." : $"This file is {LogViewer.Size(len)}, too large to show here.", TextSize.Small, false, Muted),
            Btn("Open file", () => PathUtil.OpenFile(path), false, "open").With(b => b.HorizontalAlignment = HorizontalAlignment.Left) }
    };
}
