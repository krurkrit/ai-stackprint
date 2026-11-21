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
    /// <summary>The subpages in the side menu while Settings is open: name, icon. About comes last, at the bottom of the menu.</summary>
    public static readonly (string Name, string Icon)[] Pages =
    [
        ("Appearance", "text_font"), ("Apps", "eye_off"), ("Usage limits", "data_pie"), ("Updates", "arrow_sync"), ("Benchmarks", "brain_circuit"), ("Export and import", "save")
    ];

    /// <summary>One subpage (<see cref="Pages"/> or "About").</summary>
    public Control Build(string page) => page switch
    {
        "Apps" => One("App visibility", Visibility()),
        "Usage limits" => One(page, LimitsView.Settings(x)),
        "Updates" => One(page, UpdateView.Settings(x)),
        "Benchmarks" => One(page, BenchmarksSection.Build(x)),
        "Export and import" => One(page, TransferSection.Build(x)),
        "About" => One(page, CreditsSection.Build(x)),
        _ => One("Appearance", AppearanceSection.Build(x))
    };

    /// <summary>Every section on one page (tests and screenshots).</summary>
    public Control Build() =>
        Page(Head().With(h => h.Children.Add(H1("Settings"))), AppearanceSection.Build(x), LimitsView.Settings(x), UpdateView.Settings(x), BenchmarksSection.Build(x), TransferSection.Build(x),
            Visibility(), CreditsSection.Build(x));

    private static Control One(string title, Control section) => Page(Head().With(h => h.Children.Add(H1(title))), section);

    private Control Visibility()
    {
        var st = x.S.Settings;
        var notInstalled = new ToggleSwitch
        {
            IsChecked = st.HideNotInstalled,
            OnContent = "Hide apps that are not installed",
            OffContent = "Hide apps that are not installed"
        };
        notInstalled.IsCheckedChanged += (_, _) => Save(() => st.HideNotInstalled = notInstalled.IsChecked == true);

        var rows = x.S.Defs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(d =>
        {
            var hidden = st.HiddenApps.Contains(d.Id);
            var info = x.Info(d.Id);
            var shown = new ToggleSwitch
            {
                IsChecked = !hidden,
                OnContent = "Shown",
                OffContent = "Hidden"
            };
            shown.IsCheckedChanged += (_, _) => Save(() =>
            {
                st.HiddenApps.Remove(d.Id);
                if (shown.IsChecked != true)
                {
                    st.HiddenApps.Add(d.Id);
                }
            });
            var status = hidden ? Txt("Not scanned", TextSize.Small, false, Muted) : info?.Installed == true ? Status("Installed") : Txt("Not found", TextSize.Small, false, Muted);
            var origin = d.Origin == AppOrigin.Custom ? "Custom" : "Built-in";
            return new TableRow([AppName(d.Logo, d.Category, d.Name), Txt(AppsView.Cap(d.Category), TextSize.Small, false, Muted), Txt(origin, TextSize.Small, false, Muted), status, shown]);
        });
        var table = Table.Build([new Col("App", "2*"), new Col("Category", "1*"), new Col("Source", "1*"), new Col("Status", "1*"), new Col("Visibility", "1*")], rows);

        return Section("eye_off", "App visibility", new StackPanel
        {
            Spacing = Space.L,
            Children = {
            Txt("Hidden apps are skipped when scanning and left out of the dashboard, lists and projects. Saved to " + SettingsStore.File, TextSize.Small, false, Muted),
            notInstalled, table }
        });
    }

    private void Save(Action change)
    {
        change();
        SettingsStore.Save(x.S.Settings);
        x.SettingsChanged = true;   // rescanned when leaving Settings, so the list keeps its scroll position
    }
}
