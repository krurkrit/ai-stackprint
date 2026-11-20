// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Project page: for each app with a context rule, the instruction files it loads in this project, in order, when (at launch, on demand,
/// for matching files), their size and the running total against the app's limit, with warnings. Click a file to read it here; @imports are listed under it.
/// </summary>
internal partial class ContextSection(Ctx x)
{
    private string? _project, _start, _preview;
    private object? _scan;   // the scan the cached reports belong to: a rescan reads the files again
    private ContentControl _host = new();

    /// <summary>The page was left: new controls for the next visit, so nothing here keeps the old page in memory (see <see cref="ItemBrowser.Leave"/>).</summary>
    public void Leave() => _host = new();
    private List<(AppDefinitionRef App, ContextReport Report)>? _last;

    /// <param name="Model">The model the app used last (its newest log with usage), for the share of its context window.</param>
    private sealed record AppDefinitionRef(string Id, string Name, string? Logo, string Category, string? Model = null);

    public Control Build(string project)
    {
        if (_project != project)
        {
            _project = project;
            _start = project;
            _preview = null;
            _last = null;
        }
        if (!ReferenceEquals(_scan, x.S.Items))
        {
            _scan = x.S.Items;
            _last = null;
        }
        var apps = x.S.Defs.Where(d => ContextBudget.Find(d.Context) != null && x.ShownApps.Any(a => a.Id == d.Id))
            .Select(d => new AppDefinitionRef(d.Id, d.Name, d.Logo, d.Category)).ToList();
        if (apps.Count == 0)
        {
            return new Panel();
        }

        string start = _start!, home = PathUtil.Home;
        if (_last != null)
        {
            _host.Content = Show(_last);
        }
        else
        {
            var projects = x.S.Projects.ToList();
            Busy.Into(_host, "Reading instruction files…", ct => apps.Select(a => (a with { Model = UsageStats.Supports(x.S.Def(a.Id)!) ? UsageStats.LastModel(LogReader.Find(x.S.Def(a.Id)!, projects, usage: true), ct) : null },
                ContextBudget.Find(x.S.Def(a.Id)!.Context)!.Read(project, start, home))).ToList(),
            r =>
            {
                _last = r;
                return Show(r);
            });
        }

        var pick = Btn(ContextBudget.Same(start, project) ? "Start in…" : "Start in: " + Path.GetRelativePath(project, start), () => { }, false, "folder_open");
        pick.Click += (_, _) => StartMenu(project, pick);
        ToolTip.SetTip(pick, "Which files load depends on the folder a session starts in: a folder with its own instruction files, or any folder inside the project.");
        var reset = ContextBudget.Same(start, project) ? null : IconBtn("dismiss", "Start in the project folder", () =>
        {
            _start = project;
            _last = null;
            x.Refresh();
        });
        var export = Btn("Export to AGENTS.md", () => Export(project), false, "document_edit");
        ToolTip.SetTip(export, "Combine the instruction files the apps load here into one AGENTS.md. You see it first; an existing AGENTS.md is backed up before it is replaced.");
        return Section("brain_circuit", "Loaded into context", _host.Free(), reset == null ? [export, pick] : [export, pick, reset]);
    }

    private StackPanel Show(List<(AppDefinitionRef App, ContextReport Report)> reports)
    {
        var sp = new StackPanel { Spacing = Space.XXXL };
        var shown = reports.Where(r => r.Report.Files.Count > 0 || x.Info(r.App.Id)?.Installed == true).ToList();
        if (shown.Count(r => r.Report.Files.Count > 0) > 1)
        {
            sp.Children.Add(Pills([("By app", "app"), ("By file", "file")], _byFile ? "file" : "app", v =>
            {
                _byFile = v == "file";
                _host.Content = Show(reports);
            }));
        }

        if (_byFile && shown.Count(r => r.Report.Files.Count > 0) > 1)
        {
            sp.Children.Add(Matrix(shown));
            return sp;
        }

        foreach (var (app, r) in shown)
        {
            sp.Children.Add(App(app, r));
        }

        if (sp.Children.Count == 0)
        {
            sp.Children.Add(Txt("No instruction files found for the installed apps.", TextSize.Small, false, Muted));
        }

        return sp;
    }

    private StackPanel App(AppDefinitionRef app, ContextReport r)
    {
        var sp = new StackPanel { Spacing = Space.L };
        var launch = r.Files.Count(f => f.Load is LoadWhen.Always or LoadWhen.Imported);
        var total = r.AlwaysTotal;
        sp.Children.Add(Row(AppName(app.Logo, app.Category, app.Name, Sizes.LogoS),
            Txt(r.Files.Count == 0 ? "No instruction files here" : $"{ReadableLog.Plural(launch, "file")} at launch · {total:N0} {r.Unit} · ≈{total / 4:N0} tokens{Share(total / 4, app.Model)}", TextSize.Small, false, Muted)));
        if (r.Limit is { } limit && r.Files.Count > 0)
        {
            sp.Children.Add(Bar(total, limit, r));
        }

        foreach (var f in r.Findings)
        {
            sp.Children.Add(Row(Icon(f.Warning ? "warning" : "info", IconSize.S).Themed(TextElement.ForegroundProperty, f.Warning ? WarningKey : Muted), Txt(f.Text, TextSize.Small)));
        }

        if (r.Files.Count == 0)
        {
            return sp;
        }

        long running = 0;
        var rows = r.Files.Select(f =>
        {
            var counted = f.Counted && f.Load is LoadWhen.Always or LoadWhen.Imported;
            if (counted)
            {
                running += r.Measure(f);
            }

            var name = Txt(ShortPath(f.Path), TextSize.Small, true).With(t =>
            {
                t.FontFamily = Theme.Mono;
                if (f.Via != null) { t.Margin = new Thickness(Space.XL, 0, 0, 0); }
            });
            if (f.Via != null)
            {
                ToolTip.SetTip(name, "Imported by " + f.Via);
            }

            return new TableRow(
            [
                name, Txt(Label(f.Load), TextSize.Small), Txt($"{(r.Unit == "bytes" ? f.Bytes : f.Chars):N0}", TextSize.Small), Txt($"{f.Tokens:N0}", TextSize.Small, false, Muted),
                Txt(counted ? $"{running:N0}" : "", TextSize.Small, false, r.Limit is { } l && running > l && counted ? ErrorKey : Muted), Txt(f.Note ?? "", TextSize.Caption, false, Muted)
            ], () =>
            {
                _preview = _preview == f.Path ? null : f.Path;
                if (_last != null) { _host.Content = Show(_last); }
            }, _preview == f.Path);
        }).ToList();
        sp.Children.Add(Table.Build([ new Col("File", "2.4*"), new Col("Loads", "1.1*"), new Col(r.Unit == "bytes" ? "Bytes" : "Characters", ".8*", true),
            new Col("≈Tokens", ".7*", true), new Col("Running total", ".9*", true), new Col("Note", "1.8*") ], rows));
        if (_preview != null && r.Files.Any(f => f.Path == _preview))
        {
            sp.Children.Add(Preview(_preview));
        }

        return sp;
    }

    /// <summary>" · 0.4% of claude-opus-5-5's 1M window", or nothing when the model or its window is unknown.</summary>
    internal static string Share(long tokens, string? model) =>
        model != null && ModelCatalog.Info(model)?.ContextWindow is > 0 and var window
            ? $" · {100.0 * tokens / window:0.#}% of {model}'s {UsageCard.Fmt(window)} window"
            : "";

    private static string Label(LoadWhen w) => w switch
    {
        LoadWhen.Always => "At launch",
        LoadWhen.Imported => "At launch (import)",
        LoadWhen.OnDemand => "On demand",
        LoadWhen.Conditional => "Matching files",
        LoadWhen.AgentDecides => "Agent decides",
        LoadWhen.Manual => "When mentioned",
        _ => "Ignored"
    };

    /// <summary>Use of the limit: accent up to 75 %, warning to 100 %, error over it.</summary>
    private static StackPanel Bar(long used, long limit, ContextReport r)
    {
        var pct = limit == 0 ? 0 : 100.0 * used / limit;
        var g = new Grid
        {
            Height = Sizes.BarThin,
            ColumnDefinitions = new ColumnDefinitions($"{Math.Max(Math.Min(pct, 100), 0.01)}*,{Math.Max(100 - pct, 0.01)}*")
        };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(Radius.S) }.Themed(Border.BackgroundProperty, pct > 100 ? ErrorKey : pct >= 75 ? WarningKey : Accent));
        g.Children.Add(new Border { CornerRadius = new CornerRadius(Radius.S) }.Themed(Border.BackgroundProperty, Subtle).With(b => Grid.SetColumn(b, 1)));
        return new StackPanel
        {
            Spacing = Space.XS,
            Children = { g, Txt($"{pct:0}% of {limit:N0} {r.Unit}. {r.LimitNote}", TextSize.Caption, false, Muted) }
        };
    }

    private string ShortPath(string path) =>
        _project != null && ContextBudget.Inside(path, _project) ? Path.GetRelativePath(_project, path)
        : ContextBudget.Inside(path, PathUtil.Home) ? "~" + Path.DirectorySeparatorChar + Path.GetRelativePath(PathUtil.Home, path) : path;

    /// <summary>The clicked file, rendered; its links open in the same preview.</summary>
    private Border Preview(string path)
    {
        var view = FileView.Show(path, false, link =>
        {
            if (SkillDoc.IsExternal(link))
            {
                PathUtil.OpenFile(link);
                return;
            }
            try
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", Uri.UnescapeDataString(link.Split('#')[0])));
                if (File.Exists(target))
                {
                    _preview = target;
                    if (_last != null)
                    {
                        _host.Content = Show(_last);
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        }, out var scrolls);
        var body = scrolls ? new ScrollViewer
        {
            Content = view,
            MaxHeight = Heights.Viewer
        } : view.With(v => v.Height = Heights.Viewer);
        return new Border
        {
            CornerRadius = new CornerRadius(Radius.L),
            Padding = new Thickness(Space.XL),
            Child = new StackPanel
            {
                Spacing = Space.M,
                Children =
        {
            Row(Txt(ShortPath(path), TextSize.Small, true).With(t => t.FontFamily = Theme.Mono), IconBtn("open", "Open file", () => PathUtil.OpenFile(path)), IconBtn("copy", "Copy path", () => Toast.Copy(x.Owner, path))),
            body
        }
            }
        }.Themed(Border.BackgroundProperty, Surface);
    }
}
