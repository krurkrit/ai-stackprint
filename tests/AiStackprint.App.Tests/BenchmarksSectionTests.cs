// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class BenchmarksSectionTests
{
    private static Window Open(UiState state, bool enabled)
    {
        var s = new AppState { State = state };
        s.Settings.BenchmarksEnabled = enabled;
        var w = new Window
        {
            Width = 1000,
            Height = 700
        };
        w.Content = new ScrollViewer { Content = BenchmarksSection.Build(new Ctx(s, w, _ => { }, () => { })) };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    [AvaloniaFact]
    public void Without_a_key_the_card_offers_Save_key_only_and_says_no_key_is_saved()
    {
        var w = Open(new UiState(), false);
        var texts = Ui.Texts(w);
        Assert.Contains("No key saved.", texts);
        Assert.Contains("Benchmarks", texts);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), b => Ui.Texts(b).Contains("Remove key"));
        Assert.Contains(w.GetVisualDescendants().OfType<Button>(), b => Ui.Texts(b).Contains("Save key"));
    }

    [AvaloniaFact]
    public void With_a_key_the_card_never_shows_it_and_offers_Remove_key()
    {
        var w = Open(new UiState
        {
            BenchmarkKey = "aa_secret-value",
            BenchmarkCheckedUtc = new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc),
            BenchmarkResult = "742 models saved."
        }, true);
        var texts = Ui.Texts(w);
        Assert.Contains("A key is saved.", texts);
        Assert.DoesNotContain(texts, t => t.Contains("aa_secret-value"));
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<TextBox>(), b => (b.Text ?? "").Contains("aa_secret-value"));
        Assert.Contains(w.GetVisualDescendants().OfType<Button>(), b => Ui.Texts(b).Contains("Remove key"));
        Assert.Contains(texts, t => t.StartsWith("Last download", StringComparison.Ordinal) && t.EndsWith("742 models saved.", StringComparison.Ordinal));
        Assert.Equal('•', w.GetVisualDescendants().OfType<TextBox>().Single().PasswordChar);
    }
}
