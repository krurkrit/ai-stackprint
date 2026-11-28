// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace AiStackprint.Ui;

public class AiStackprintApp : Application
{
    public override void Initialize()
    {
        Styles.Add(Theme.Fluent());   // Fluent controls with the app's palette; variant follows the OS (ThemeVariant.Default)
        Styles.Add(Theme.Controls());   // Fluent 2 control templates; our accent and palette stay
        Styles.Add(CodeView.EditorStyles());
        Motion.Install();   // pop-ups fade and slide in
        Theme.Appearance(this, Services.SettingsStore.Defaults());   // defaults until MainWindow loads settings.json
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
        {
            Motion.Disabled = d.Args?.Contains("--no-animations") == true;   // with --perf: measure without motion, to compare with a run that has it
            d.MainWindow = new MainWindow { PerfRun = d.Args?.Contains("--perf") == true };   // --perf: measure every page, log it and close
        }

        base.OnFrameworkInitializationCompleted();
    }
}
