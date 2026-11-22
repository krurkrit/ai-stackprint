// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using AiStackprint.Services;
using AiStackprint.Ui;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>A step that a search result, bookmark or error goes to is tinted and fades out, in cards and in Compact, whether or not it was already on screen.</summary>
public sealed class RevealHighlightTests
{
    private static Session Session() => new ClaudeSessionParser().Parse(new[]
    {
        """{"type":"user","timestamp":"2026-11-11T05:00:00Z","message":{"role":"user","content":"first"}}""",
        """{"type":"assistant","timestamp":"2026-11-11T05:00:01Z","message":{"id":"m1","content":[{"type":"text","text":"second"}]}}"""
    }.Select(l => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l)));

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_revealed_step_is_tinted_even_when_it_is_already_in_view(bool compact)
    {
        var log = new ReadableLog(Session(), false, compact);
        var w = Ui.Show(log.Root);
        var step = log.Steps[1];
        int Tints() => w.GetVisualDescendants().OfType<Border>().Count(b => !b.IsHitTestVisible && b.Parent is Grid);
        var before = Tints();
        log.Reveal(step, 5);
        Assert.True(Tints() > before);
        w.Close();
    }
}
