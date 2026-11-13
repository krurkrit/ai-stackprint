using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AiStackprint.Models;
using AiStackprint.Services;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Settings › Appearance: font family, base size, weights, log font and colors (the theme is switched in the side menu). Changes apply at once and are saved.</summary>
static class AppearanceSection
{
    /// <summary>One list for Body and Emphasis weight.</summary>
    static readonly (string Label, int Value)[] Weights =
        { ("Light (300)", 300), ("Regular (400)", 400), ("Medium (500)", 500), ("SemiBold (600)", 600), ("Bold (700)", 700) };

    /// <summary>Typed whole-number size, so one digit at a time works; the value is checked when the box is left or Enter is pressed.</summary>
    static NumericUpDown SizeBox(double value, int min, int max, Action<double> set)
    {
        var box = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", MinWidth = 120, Value = (decimal)Math.Clamp(Math.Round(value), min, max) };
        box.ValueChanged += (_, e) =>
        {
            if (e.NewValue is not { } v) return;
            var n = Math.Clamp(Math.Round((double)v), min, max);
            if (n != value) { value = n; set(n); }
        };
        return box;
    }

    public static Control Build(Ctx x)
    {
        var st = x.S.Settings;
        void Change(Action a) { a(); SettingsStore.Save(st); x.Restyle(); }

        var installed = FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
        var labels = Weights.Select(w => w.Label).ToList();
        string WLabel(int v, int fallback) => Weights.FirstOrDefault(w => w.Value == v).Label ?? Weights.First(w => w.Value == fallback).Label;
        int WValue(string l) => Weights.First(w => w.Label == l).Value;
        var monoKnown = new[] { "JetBrains Mono", "Cascadia Mono", "Cascadia Code", "Consolas", "Fira Code", "Fira Mono", "Source Code Pro", "Menlo", "Monaco", "SF Mono", "DejaVu Sans Mono", "Ubuntu Mono", "Courier New" };
        var monos = installed.Where(f => monoKnown.Contains(f, StringComparer.OrdinalIgnoreCase) || f.Contains("Mono", StringComparison.OrdinalIgnoreCase) || f.Contains("Code", StringComparison.OrdinalIgnoreCase)).ToList();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), RowSpacing = 12, ColumnSpacing = 16 };
        void Add(string label, Control c, string? hint = null)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            int r = grid.RowDefinitions.Count - 1;
            var l = Txt(label, 14, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center);
            Control v = hint == null ? c : Row(c, Txt(hint, 12, false, Muted).With(t => t.VerticalAlignment = VerticalAlignment.Center));
            Grid.SetRow(l, r); Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            grid.Children.Add(l); grid.Children.Add(v);
        }
        void Group(string name) { Add("", new Panel()); grid.Children.RemoveAt(grid.Children.Count - 2); grid.Children.Add(Title(name, 14).With(t => { Grid.SetRow(t, grid.RowDefinitions.Count - 1); Grid.SetColumnSpan(t, 2); })); }

        Group("Body");
        Add("Font family", FontCombo(installed, "Inter", st.FontFamily, f => Change(() => st.FontFamily = f)), installed.Contains("Inter") ? null : "Not installed: Noto Sans or the system font is used. Get Inter at rsms.me/inter");
        Add("Font size", SizeBox(st.FontSize, 12, 18, n => Change(() => st.FontSize = n)), "12 to 18 px; titles and labels scale with it");
        Add("Weight", Combo(labels, WLabel(st.BodyWeight, 400), l => Change(() => st.BodyWeight = WValue(l))));
        Add("Emphasis weight", Combo(labels, WLabel(st.EmphasisWeight, 500), l => Change(() => st.EmphasisWeight = WValue(l))), "Names and labels");
        Group("Headings");
        Add("Font family", FontCombo(installed, "Same as body", string.IsNullOrWhiteSpace(st.HeadingFontFamily) ? "Same as body" : st.HeadingFontFamily, f => Change(() => st.HeadingFontFamily = f == "Same as body" ? "" : f), "Same as body"));
        var headWeights = labels.Prepend("Automatic").ToList();
        Add("Weight", Combo(headWeights, st.HeadingWeight == 0 ? "Automatic" : WLabel(st.HeadingWeight, 600), l => Change(() => st.HeadingWeight = l == "Automatic" ? 0 : WValue(l))), "Sizes scale with Body");
        Group("Code & logs");
        Add("Font family", FontCombo(monos.Concat(monoKnown).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), "JetBrains Mono", st.LogFontFamily, f => Change(() => st.LogFontFamily = f), installedOnly: installed), "Logs and the config editor");
        Add("Font size", SizeBox(st.LogFontSize, 10, 18, n => Change(() => st.LogFontSize = n)), "10 to 18 px");
        Add("Weight", Combo(labels, WLabel(st.LogFontWeight, 400), l => Change(() => st.LogFontWeight = WValue(l))));
        var lines = new ToggleSwitch { IsChecked = st.LogLineNumbers, OnContent = "Shown", OffContent = "Hidden" };
        lines.IsCheckedChanged += (_, _) => Change(() => st.LogLineNumbers = lines.IsChecked == true);
        Add("Line numbers", lines, "Logs only; the config editor always shows them");
        Add("New log lines", SizeBox(st.FadeSeconds, 0, 60, n => { st.FadeSeconds = (int)n; SettingsStore.Save(st); }), "Seconds new lines and steps stay highlighted while they fade; 0 = off");
        Group("Side menu");
        Add("Weight", Combo(labels, WLabel(st.MenuWeight, 500), l => Change(() => st.MenuWeight = WValue(l))));

        var preview = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(14), Child = new StackPanel { Spacing = 6, Children =
        {
            Title("Claude Code", 22), Txt("Skills that apply in this project, with their level and source.", 14),
            Txt("Emphasis text · format-code · Project level", 14, true), Txt("Muted detail text", 12, false, Muted)
        } } }.Themed(Border.BackgroundProperty, Background);

        var reset = Btn("Reset to defaults", () => Change(() =>
        {
            var d = new Settings();
            st.FontFamily = d.FontFamily; st.FontSize = d.FontSize; st.BodyWeight = d.BodyWeight; st.EmphasisWeight = d.EmphasisWeight;
            st.LogFontFamily = d.LogFontFamily; st.LogFontSize = d.LogFontSize; st.LogFontWeight = d.LogFontWeight; st.HeadingFontFamily = d.HeadingFontFamily; st.HeadingWeight = d.HeadingWeight;
            st.MenuWeight = d.MenuWeight; st.LogLineNumbers = d.LogLineNumbers; st.Light = new(); st.Dark = new();
        }), false, "arrow_reset");
        var colors = new StackPanel { Spacing = 8, Children = { Title("Colors", 14), Txt("Empty = Snowy Owl default (shown grayed). Type #RRGGBB, or click the swatch to pick; opacity is separate.", 12, false, Muted),
            ColorSettings.Build(st, Change) } };
        return Section("text_font", "Appearance", new StackPanel { Spacing = 16, Children = { grid, preview, colors } }, reset);
    }

    const string NotInstalled = " (not installed)";

    /// <summary>Font list; the chosen font and any listed font that is not installed are marked "(not installed)".</summary>
    static ComboBox FontCombo(List<string> fonts, string fallback, string current, Action<string> pick, string? first = null, List<string>? installedOnly = null)
    {
        var have = installedOnly ?? fonts;
        bool Has(string f) => f == first || have.Contains(f, StringComparer.OrdinalIgnoreCase);
        var names = fonts.ToList();
        if (!names.Contains(current) && !names.Contains(fallback)) names.Insert(0, current);
        if (!names.Contains(fallback) && first == null) names.Insert(0, fallback);
        if (first != null && !names.Contains(first)) names.Insert(0, first);
        string Label(string f) => Has(f) ? f : f + NotInstalled;
        var items = names.Select(Label).ToList();
        var c = Combo(items, Label(current), l => pick(l.EndsWith(NotInstalled) ? l[..^NotInstalled.Length] : l));
        return c;
    }

    internal static ComboBox Combo(List<string> items, string current, Action<string> pick)
    {
        var c = new ComboBox { ItemsSource = items, SelectedItem = items.Contains(current) ? current : items.FirstOrDefault(), MinWidth = 220 };
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is string s && s != current) pick(s); };
        return c;
    }
}
