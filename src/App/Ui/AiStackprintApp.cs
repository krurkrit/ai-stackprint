// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;

namespace AiStackprint.Ui;

public class AiStackprintApp : Application
{
    public override void Initialize()
    {
        Styles.Add(Theme.Fluent());   // Fluent controls with the Snowy Owl palette; variant follows the OS (ThemeVariant.Default)
        Styles.Add(CodeView.EditorStyles());
        // Text boxes, number boxes and drop-downs share one height
        Styles.Add(new Style(x => x.OfType<Avalonia.Controls.ComboBox>()) { Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.TemplatedControl.MinHeightProperty, 36d) } });
        Styles.Add(new Style(x => x.OfType<Avalonia.Controls.TextBox>()) { Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.TemplatedControl.MinHeightProperty, 36d) } });
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
