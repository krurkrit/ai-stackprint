// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Preferences: appearance, and which apps are shown. Hidden apps are not scanned and do not appear anywhere else.</summary>
internal class SettingsView(Ctx x)
{
    public Control Build()
    {
        var st = x.S.Settings;
        var notInstalled = new ToggleSwitch { IsChecked = st.HideNotInstalled, OnContent = "Hide apps that are not installed", OffContent = "Hide apps that are not installed" };
        notInstalled.IsCheckedChanged += (_, _) => Save(() => st.HideNotInstalled = notInstalled.IsChecked == true);

        var rows = x.S.Defs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(d =>
        {
            var hidden = st.HiddenApps.Contains(d.Id);
            var info = x.Info(d.Id);
            var shown = new ToggleSwitch { IsChecked = !hidden, OnContent = "Shown", OffContent = "Hidden" };
            shown.IsCheckedChanged += (_, _) => Save(() =>
            {
                st.HiddenApps.Remove(d.Id);
                if (shown.IsChecked != true)
                {
                    st.HiddenApps.Add(d.Id);
                }
            });
            Control status = hidden ? Txt("Not scanned", 13, false, Muted) : info?.Installed == true ? Status("Installed") : Txt("Not found", 13, false, Muted);
            var origin = d.Origin switch { AppOrigin.Custom => "Custom", AppOrigin.Override => "Customised", _ => "Built-in" };
            return new TableRow([AppName(d.Logo, d.Category, d.Name), Txt(AppsView.Cap(d.Category), 13, false, Muted), Txt(origin, 13, false, Muted), status, shown]);
        });
        var table = Table.Build([new Col("App", "2*"), new Col("Category", "1*"), new Col("Source", "1*"), new Col("Status", "1*"), new Col("Visibility", "1*")], rows);

        return Page(H1("Settings"), AppearanceSection.Build(x), LimitsView.Settings(x), UpdateView.Settings(x),
            Section("eye_off", "App visibility", new StackPanel
            {
                Spacing = 12,
                Children = {
                Txt("Hidden apps are skipped when scanning and left out of the dashboard, lists and projects. Saved to " + SettingsStore.File, 13, false, Muted),
                notInstalled, table }
            }));
    }

    private void Save(Action change)
    {
        change();
        SettingsStore.Save(x.S.Settings);
        x.SettingsChanged = true;   // rescanned when leaving Settings, so the list keeps its scroll position
    }
}
