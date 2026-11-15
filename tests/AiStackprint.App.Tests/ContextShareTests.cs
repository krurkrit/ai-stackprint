// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Headless.XUnit;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>The context window in the UI: the project page's share and the Logs page's peak context (built-in model data).</summary>
public sealed class ContextShareTests
{
    [AvaloniaFact]
    public void The_project_page_shows_the_share_of_the_last_model_s_window_or_nothing()
    {
        Assert.Equal(" · 0.5% of claude-opus-5-5's 1M window", ContextSection.Share(5_000, "claude-opus-5-5"));
        Assert.Equal("", ContextSection.Share(5_000, null));
        Assert.Equal("", ContextSection.Share(5_000, "no-such-model"));
    }

    [AvaloniaFact]
    public void A_session_shows_its_peak_against_the_logged_window_else_the_model_s()
    {
        static Session S(long peak, long window, params string[] models) => new([], [.. models], 0, 0, null, null, peak, window);
        Assert.Equal("Peak context 120K of 258.4K (46%)", ReadableLog.PeakContext(S(120_000, 258_400, "gpt-6.1-sol")));
        Assert.Equal("Peak context 250K of 1M (25%)", ReadableLog.PeakContext(S(250_000, 0, "claude-opus-5-5")));
        Assert.Equal("Peak context 5K", ReadableLog.PeakContext(S(5_000, 0, "no-such-model")));
        Assert.Null(ReadableLog.PeakContext(S(0, 0)));
    }
}
