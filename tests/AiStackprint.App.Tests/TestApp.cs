// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.App.Tests;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(TestApp))]
// One test at a time: UI tests are few and fast, and none waits on another class's app.
// Every test that touches Avalonia (Theme too) must be [AvaloniaFact]/[AvaloniaTheory]: a plain test on a pool thread made Avalonia's
// render loop belong to that thread, and the next UI test failed to start ("a different thread owns it").
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace AiStackprint.App.Tests;

/// <summary>
/// The real app (theme, styles, fonts) on Avalonia's headless platform: views run without a window or screen.
/// Skia lays out text with real font metrics, so tests can check where text sits. No MainWindow is created (there is no desktop lifetime), so nothing scans this machine. Tests use [AvaloniaFact].
/// </summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AiStackprintApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
