// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Models;
using AiStackprint.Services;

using Avalonia.Controls;
using Avalonia.Controls.Documents;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// The font setting control: one compact row for one font setting (Body, Headings, …): a parent drop-down, then family, size and weight, each behind a button that switches
/// how the field gets its value (own value, the parent's, or the parent's plus an offset). The label is the page's: it sits outside the control.
/// The rules and their limits are in <see cref="FontRuleSet"/>; this only shows and edits them. Reuse it for any setting key of the set.
/// </summary>
internal sealed partial class FontSetting
{
    private readonly FontRuleSet _set;
    private readonly string _key;
    private readonly FontRole _role;
    private readonly List<string> _families;
    private readonly Action<FontSetting> _changed;
    private readonly ContentControl _host = new();

    /// <param name="set">The font settings; an edit changes them in place.</param>
    /// <param name="key">Which setting this row edits (a key of <see cref="FontRoles"/>).</param>
    /// <param name="families">Font names the family drop-down offers.</param>
    /// <param name="changed">Called after every edit, with this control: the page saves and refreshes the other controls (their linked values moved).</param>
    public FontSetting(FontRuleSet set, string key, List<string> families, Action<FontSetting> changed)
    {
        _set = set;
        _key = key;
        _role = FontRoles.Find(key) ?? throw new ArgumentException($"Unknown font setting \"{key}\"", nameof(key));
        _families = families;
        _changed = changed;
        Refresh();
    }

    public Control View => _host;

    /// <summary>Shows the current values again (after the setting it links to changed).</summary>
    public void Refresh() => _host.Content = Layout();

    /// <summary>A change of how a field gets its value, or of the parent: the page is told and this row is built again.</summary>
    private void Edit(Action change)
    {
        change();
        _changed(this);
        Refresh();
    }

    /// <summary>A change of a value in place: the row is not built again (the box being typed in keeps focus).</summary>
    private void Value(Action change)
    {
        change();
        _changed(this);
    }

    private Control Layout()
    {
        var wrap = new WrapPanel { ItemSpacing = Space.M, LineSpacing = Space.S };
        wrap.Children.Add(ParentBox());
        wrap.Children.Add(Field(FontProp.Family, FamilyBox()));
        wrap.Children.Add(Field(FontProp.Size, SizeBox()));
        wrap.Children.Add(Field(FontProp.Weight, WeightBox()));
        return wrap;
    }

    private string ParentKey => _set.Parent(_key);

    private Control ParentBox()
    {
        const string none = "None";
        var keys = FontRoles.All.Select(r => r.Key).Where(k => _set.Has(k) && _set.ParentChoices(_key).Contains(k)).ToList();
        var items = keys.Select(FontRoles.Label).Prepend(none).ToList();
        var current = ParentKey.Length == 0 ? none : FontRoles.Label(ParentKey);
        var box = new ComboBox { ItemsSource = items, SelectedItem = items.Contains(current) ? current : none, Width = Widths.FontParent - IconSize.S - Space.S };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string s && s != current)
            {
                Edit(() => _set.SetParent(_key, s == none ? "" : keys[items.IndexOf(s) - 1]));
            }
        };
        var chain = Icon("link", IconSize.S).Themed(TextElement.ForegroundProperty, ParentKey.Length == 0 ? Muted : Accent);
        ToolTip.SetTip(box, ParentKey.Length == 0 ? "Parent: none" : $"Parent: {FontRoles.Label(ParentKey)}");
        return Row(chain, box).With(r => ((StackPanel)r).Spacing = Space.S);
    }

    /// <summary>The mode button in front of an input: its icon is the mode now; a tap goes to the next one (link, adjust, set). Disabled while there is no parent.</summary>
    private Control Field(FontProp prop, Control input)
    {
        var mode = _set.Mode(_key, prop);
        var parent = ParentKey.Length == 0 ? "" : FontRoles.Label(ParentKey);
        var (icon, tip) = mode switch
        {
            FontMode.Link => ("link_arrow", $"Use {parent}"),
            FontMode.Adjust => ("plus_minus", $"Adjust {parent}"),
            _ => ("edit", "Set")
        };
        var next = mode switch
        {
            FontMode.Link => prop == FontProp.Family ? FontMode.Set : FontMode.Adjust,
            FontMode.Adjust => FontMode.Set,
            _ => FontMode.Link
        };
        var button = IconBtn(icon, tip, () => Edit(() => _set.SetMode(_key, prop, next)));
        button.IsEnabled = parent.Length > 0;
        return Row(button, input).With(r => ((StackPanel)r).Spacing = 0);
    }
}
