// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using AiStackprint.Services;
using AiStackprint.Ui;
using Avalonia.Headless.XUnit;
using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Conversation: a reply shows its tokens and cost under its first step, and the header shows the session's estimate.</summary>
public sealed class StepUsageTests
{
    [AvaloniaFact]
    public void A_reply_shows_its_cost_on_its_line_and_the_header_the_total()
    {
        var lines = new[]
        {
            """{"type":"user","timestamp":"2026-11-11T04:59:00Z","message":{"role":"user","content":"hello"}}""",
            """{"type":"assistant","timestamp":"2026-11-11T05:00:00Z","requestId":"r1","message":{"id":"m1","model":"claude-opus-5-5","usage":{"input_tokens":10,"output_tokens":500,"cache_read_input_tokens":90000,"cache_creation_input_tokens":2000},"content":[{"type":"text","text":"hi there"}]}}"""
        }.Select(l => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(l));
        var session = new ClaudeSessionParser().Parse(lines);
        var log = new ReadableLog(session, false);
        var w = Ui.Show(log.Root);
        var texts = Ui.Texts(w);
        if (ModelCatalog.Cost(session.Entries[1].Usage!) is { } cost)
        {
            Assert.Contains(texts, t => t.Contains(ReadableLog.StepMoney(cost), StringComparison.Ordinal));
            Assert.Contains(texts, t => t.StartsWith("≈ ", StringComparison.Ordinal));
        }

        w.Close();
    }
}
