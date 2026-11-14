// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Small helpers for headless UI tests.</summary>
internal static class Ui
{
    /// <summary>Shows a control in a headless window so it is laid out and attached, like on screen.</summary>
    public static Window Show(Control c)
    {
        var w = new Window { Width = 1200, Height = 800, Content = c };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    /// <summary>Every text shown inside <paramref name="root"/>, including text built from inlines (e.g. a dot before the words).</summary>
    public static List<string> Texts(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Select(Text).Where(t => t.Length > 0)];

    private static string Text(TextBlock t) =>
        t.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : t.Text ?? "";

    public static Button Button(Visual root, string text) =>
        root.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text);

    public static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    /// <summary>Runs the UI thread's queued work until <paramref name="done"/> holds (background work posts its results back).</summary>
    public static void WaitUntil(Func<bool> done, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < until, "Timed out waiting for the UI");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
    }
}
