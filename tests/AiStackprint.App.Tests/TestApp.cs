// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.App.Tests;
using AiStackprint.Ui;

using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace AiStackprint.App.Tests;

/// <summary>
/// The real app (theme, styles, fonts) on Avalonia's headless platform: views run without a window or screen.
/// No MainWindow is created (there is no desktop lifetime), so nothing scans this machine. Tests use [AvaloniaFact].
/// </summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AiStackprintApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
