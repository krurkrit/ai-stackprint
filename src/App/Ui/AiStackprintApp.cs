// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace AiStackprint.Ui;

public class AiStackprintApp : Application
{
    public override void Initialize()
    {
        Styles.Add(Theme.Fluent());   // Fluent controls with the Snowy Owl palette; variant follows the OS (ThemeVariant.Default)
        Styles.Add(CodeView.EditorStyles());
        Theme.Appearance(this, new Models.Settings());   // defaults until MainWindow loads settings.json
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
        {
            d.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
