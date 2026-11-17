// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Project page: Claude Code's permission rules and settings from every level that sets them, with the level that wins. Shown only when a settings file exists.</summary>
internal class ClaudeSettingsSection(Ctx x)
{
    private string? _project;
    private object? _scan;
    private ClaudeSettingsInfo? _info;

    public Control Build(string project)
    {
        if (_project != project || !ReferenceEquals(_scan, x.S.Items))
        {
            _project = project;
            _scan = x.S.Items;
            _info = ClaudeSettings.Read(x.S.Defs, project, PathUtil.Home);
        }

        if (!_info!.Any)
        {
            return new Panel();
        }

        var rules = _info.Rules.Select(r => new TableRow(
        [
            Mono(r.Rule, TextSize.Small), Txt(r.Level, TextSize.Small), Chip(r.Effect, r.Effect == "deny" ? ErrorKey : r.Effect == "ask" ? WarningKey : Accent),
            Txt(r.Note ?? "", TextSize.Caption, false, r.Note?.StartsWith("Never", StringComparison.Ordinal) == true ? WarningKey : Muted), IconBtn("open", "Open file", () => PathUtil.OpenFile(r.File))
        ]));
        var settings = _info.Settings.Select(s => new TableRow(
        [
            Txt(s.Key, TextSize.Small, true), Txt(s.Level, TextSize.Small), Mono(s.Value, TextSize.Small), Txt(s.Overrides == null ? "" : "Overrides " + s.Overrides, TextSize.Caption, false, Muted),
            IconBtn("open", "Open file", () => PathUtil.OpenFile(s.File))
        ]));
        var body = new StackPanel
        {
            Spacing = Space.L,
            Children =
            {
                Txt("Levels are read highest priority first: System (managed), Local, Project, User. A deny rule at any level wins. Secret-looking values are hidden; env shows names only.", TextSize.Small, false, Muted),
                Title($"Permission rules · {_info.Rules.Count}", TextSize.Subtitle),
                Table.Build([Cols.Name("Rule", "3*"), Cols.Level(), Cols.Type("Effect"), new Col("Note", "2*"), Cols.Action()], rules, "No permission rules."),
                Title($"Settings · {_info.Settings.Count}", TextSize.Subtitle),
                Table.Build([Cols.Name("Setting", "1.4*"), Cols.Level(), new Col("Value", "3*"), new Col("Note", "2*"), Cols.Action()], settings, "No settings of this kind.")
            }
        };
        return Section("settings", "Claude Code settings", body);
    }
}
