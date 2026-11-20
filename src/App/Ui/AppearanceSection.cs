// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Settings › Appearance: font family, base size, weights, log font and colors (the theme is switched in the side menu). Changes apply at once and are saved.</summary>
internal static class AppearanceSection
{
    /// <summary>The installed font names, read once: listing the system fonts takes a moment and the list does not change while the app runs.</summary>
    private static readonly Lazy<List<string>> _installed = new(() => [.. FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase)]);

    /// <summary>Typed whole-number size, so one digit at a time works; the value is checked when the box is left or Enter is pressed.</summary>
    private static NumericUpDown SizeBox(double value, int min, int max, Action<double> set)
    {
        var box = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = 1,
            FormatString = "0",
            MinWidth = Widths.FieldS,
            Value = (decimal)Math.Clamp(Math.Round(value), min, max)
        };
        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is not { } v)
            {
                return;
            }

            var n = Math.Clamp(Math.Round((double)v), min, max);
            if (Math.Abs(n - value) > 0.001)
            {
                value = n;
                set(n);
            }
        };
        return box;
    }

    public static Control Build(Ctx x)
    {
        var st = x.S.Settings;
        void Change(Action a)
        {
            a();
            SettingsStore.Save(st);
            x.Restyle();
        }

        var installed = _installed.Value;
        var monoKnown = new[] { "JetBrains Mono", "Cascadia Mono", "Cascadia Code", "Consolas", "Fira Code", "Fira Mono", "Source Code Pro", "Menlo", "Monaco", "SF Mono", "DejaVu Sans Mono", "Ubuntu Mono", "Courier New" };
        var monos = installed.Where(f => monoKnown.Contains(f, StringComparer.OrdinalIgnoreCase) || f.Contains("Mono", StringComparison.OrdinalIgnoreCase) || f.Contains("Code", StringComparison.OrdinalIgnoreCase)).ToList();

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{Widths.Label},*"),
            RowSpacing = Space.L,
            ColumnSpacing = Space.XL
        };
        void Add(string label, Control c, string? hint = null)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var r = grid.RowDefinitions.Count - 1;
            var l = Txt(label, TextSize.Body, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center);
            var v = hint == null ? c : Row(c, Txt(hint, TextSize.Caption, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center));
            Grid.SetRow(l, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
        }
        void Group(string name)
        {
            Add("", new Panel());
            grid.Children.RemoveAt(grid.Children.Count - 2);
            grid.Children.Add(Title(name, TextSize.Body).With(t =>
            {
                Grid.SetRow(t, grid.RowDefinitions.Count - 1);
                Grid.SetColumnSpan(t, 2);
            }));
        }

        Group("Fonts");
        var set = new FontRuleSet(st.Fonts);
        var controls = new List<FontSetting>();
        foreach (var role in FontRoles.All)
        {
            var list = role.Mono ? [.. monos.Concat(monoKnown).Distinct(StringComparer.OrdinalIgnoreCase)] : installed;
            var fs = new FontSetting(set, role.Key, list, edited =>
            {
                Change(() => { });
                foreach (var other in controls.Where(c => c != edited)) { other.Refresh(); }   // what links to the changed setting moved too
            });
            controls.Add(fs);
            Add(role.Label, fs.View.With(c => ToolTip.SetTip(c, role.Note)));
        }

        Group("Logs");
        var lines = new ToggleSwitch
        {
            IsChecked = st.LogLineNumbers,
            OnContent = "Shown",
            OffContent = "Hidden"
        };
        lines.IsCheckedChanged += (_, _) => Change(() => st.LogLineNumbers = lines.IsChecked == true);
        Add("Line numbers", lines, "Logs only; the config editor always shows them");
        Add("New log lines", SizeBox(st.FadeSeconds, 0, 60, n =>
        {
            st.FadeSeconds = (int)n;
            SettingsStore.Save(st);
        }), "Seconds new lines and steps stay highlighted while they fade; 0 = off");

        var preview = new Border
        {
            CornerRadius = new CornerRadius(Radius.M),
            Padding = new Thickness(Space.XL),
            Child = new StackPanel
            {
                Spacing = Space.S,
                Children =
        {
            Title("Claude Code", TextSize.Page), Txt("Skills that apply in this project, with their level and source.", TextSize.Body),
            Txt("Emphasis text · format-code · Project level", TextSize.Body, true), Txt("Muted detail text", TextSize.Caption, false, Muted)
        }
            }
        }.Themed(Border.BackgroundProperty, Background);

        var reset = Btn("Reset to defaults", () => Change(() =>
        {
            var d = SettingsStore.Defaults();
            st.Fonts.Clear();
            foreach (var (k, v) in d.Fonts) { st.Fonts[k] = v; }
            controls.ForEach(c => c.Refresh());
            st.LogLineNumbers = d.LogLineNumbers;
            st.Light = new();
            st.Dark = new();
        }), false, "arrow_reset");
        var colors = new StackPanel
        {
            Spacing = Space.M,
            Children = { Title("Colors", TextSize.Body), Txt("Empty = Snowy Owl default (shown grayed). Type #RRGGBB, or click the swatch to pick; opacity is separate.", TextSize.Caption, false, Muted),
            ColorSettings.Build(st, Change) }
        };
        return Section("text_font", "Appearance", new StackPanel
        {
            Spacing = Space.XL,
            Children = { grid, preview, colors }
        }, reset);
    }

    private const string NotInstalled = " (not installed)";

    /// <summary>Font list; the chosen font and any listed font that is not installed are marked "(not installed)".</summary>
    internal static ComboBox FontCombo(List<string> fonts, string fallback, string current, Action<string> pick, string? first = null, List<string>? installedOnly = null, double width = Widths.Field)
    {
        var have = installedOnly ?? fonts;
        bool Has(string f) => f == first || have.Contains(f, StringComparer.OrdinalIgnoreCase);
        var names = fonts.ToList();
        if (!names.Contains(current) && !names.Contains(fallback))
        {
            names.Insert(0, current);
        }

        if (!names.Contains(fallback) && first == null)
        {
            names.Insert(0, fallback);
        }

        if (first != null && !names.Contains(first))
        {
            names.Insert(0, first);
        }

        string Label(string f) => Has(f) ? f : f + NotInstalled;
        var items = names.Select(Label).ToList();
        var c = Combo(items, Label(current), l => pick(l.EndsWith(NotInstalled) ? l[..^NotInstalled.Length] : l));
        c.MinWidth = 0;
        c.Width = width;
        return c;
    }

    internal static ComboBox Combo(List<string> items, string current, Action<string> pick)
    {
        var c = new ComboBox
        {
            ItemsSource = items,
            SelectedItem = items.Contains(current) ? current : items.FirstOrDefault(),
            MinWidth = Widths.Field
        };
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is string s && s != current) { pick(s); } };
        return c;
    }
}
