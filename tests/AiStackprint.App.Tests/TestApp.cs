// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.App.Tests;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(TestApp))]
// One headless app for all tests. With one app per test (the default), setting up the next app sometimes found the shared render loop
// still owned by the previous test's UI thread ("a different thread owns it"), on Linux CI even with one test at a time.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
// One test at a time: tests share that app (its theme and appearance settings). UI tests are few and fast.
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
