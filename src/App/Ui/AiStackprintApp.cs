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
        Theme.Appearance(this, Services.SettingsStore.Defaults());   // defaults until AppHost loads settings.json
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
        {
            var args = d.Args ?? [];
            Motion.Disabled = args.Contains("--no-animations");   // with --perf: measure without motion, to compare with a run that has it
            Host = new AppHost(new DesktopLifetime(d), args);   // --perf: measure every page, log it and close; --tray: start in the tray (the login entry)
            Host.Start(args);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The running app; null under the test platform (no desktop lifetime).</summary>
    internal static AppHost? Host { get; private set; }
}
