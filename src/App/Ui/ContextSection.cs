using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using AiStackprint.Services;
using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>
/// Project page: for each app with a context rule, the instruction files it loads in this project, in order, when (at launch, on demand,
/// for matching files), their size and the running total against the app's limit, with warnings. Click a file to read it here; @imports are listed under it.
/// </summary>
class ContextSection(Ctx x)
{
    string? _project, _start, _preview;
    object? _scan;   // the scan the cached reports belong to: a rescan reads the files again
    readonly ContentControl _host = new();
    List<(AppDefinitionRef App, ContextReport Report)>? _last;

    record AppDefinitionRef(string Id, string Name, string? Logo, string Category);

    public Control Build(string project)
    {
        if (_project != project) { _project = project; _start = project; _preview = null; _last = null; }
        if (!ReferenceEquals(_scan, x.S.Items)) { _scan = x.S.Items; _last = null; }
        var apps = x.S.Defs.Where(d => ContextBudget.Find(d.Context) != null && x.ShownApps.Any(a => a.Id == d.Id))
            .Select(d => new AppDefinitionRef(d.Id, d.Name, d.Logo, d.Category)).ToList();
        if (apps.Count == 0) return new Panel();
        string start = _start!, home = PathUtil.Home;
        if (_last != null) _host.Content = Show(_last);
        else Busy.Into(_host, "Reading instruction files…", _ => apps.Select(a => (a, ContextBudget.Find(x.S.Def(a.Id)!.Context)!.Read(project, start, home))).ToList(),
            r => { _last = r; return Show(r); });
        var pick = Btn(ContextBudget.Same(start, project) ? "Start in…" : "Start in: " + Path.GetRelativePath(project, start), () => PickStart(project), false, "folder_open");
        ToolTip.SetTip(pick, "Which files load depends on the folder a session starts in. Pick a folder inside the project.");
        var reset = ContextBudget.Same(start, project) ? null : IconBtn("dismiss", "Start in the project folder", () => { _start = project; _last = null; x.Refresh(); });
        return Section("brain_circuit", "Loaded into context", _host.Free(), reset == null ? new Control[] { pick } : new Control[] { pick, reset });
    }

    async void PickStart(string project)
    {
        var start = await x.Owner.StorageProvider.TryGetFolderFromPathAsync(project);
        var r = await x.Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Session starts in", SuggestedStartLocation = start });
        if (r.Count == 0 || r[0].TryGetLocalPath() is not { } p) return;
        if (!ContextBudget.Same(p, project) && !ContextBudget.Inside(p, project)) { Toast.Show("Pick a folder inside the project", "warning"); return; }
        _start = p; _last = null; _preview = null;
        x.Refresh();
    }

    Control Show(List<(AppDefinitionRef App, ContextReport Report)> reports)
    {
        var sp = new StackPanel { Spacing = 22 };
        foreach (var (app, r) in reports.Where(r => r.Report.Files.Count > 0 || x.Info(r.App.Id)?.Installed == true))
            sp.Children.Add(App(app, r));
        if (sp.Children.Count == 0) sp.Children.Add(Txt("No instruction files found for the installed apps.", 13, false, Muted));
        return sp;
    }

    Control App(AppDefinitionRef app, ContextReport r)
    {
        var sp = new StackPanel { Spacing = 10 };
        int launch = r.Files.Count(f => f.Load is LoadWhen.Always or LoadWhen.Imported);
        long total = r.AlwaysTotal;
        sp.Children.Add(Row(AppName(app.Logo, app.Category, app.Name, 24),
            Txt(r.Files.Count == 0 ? "No instruction files here" : $"{ReadableLog.Plural(launch, "file")} at launch · {total:N0} {r.Unit} · ≈{total / 4:N0} tokens", 13, false, Muted)));
        if (r.Limit is { } limit && r.Files.Count > 0) sp.Children.Add(Bar(total, limit, r));
        foreach (var f in r.Findings)
            sp.Children.Add(Row(Icon(f.Warning ? "warning" : "info", 14).Themed(TextElement.ForegroundProperty, f.Warning ? WarningKey : Muted), Txt(f.Text, 13)));
        if (r.Files.Count == 0) return sp;

        long running = 0;
        var rows = r.Files.Select(f =>
        {
            bool counted = f.Counted && f.Load is LoadWhen.Always or LoadWhen.Imported;
            if (counted) running += r.Measure(f);
            var name = Txt(Show(f.Path), 13, true).With(t => { t.FontFamily = Ui.Theme.Mono; if (f.Via != null) t.Margin = new Thickness(16, 0, 0, 0); });
            if (f.Via != null) ToolTip.SetTip(name, "Imported by " + f.Via);
            return new TableRow(new Control[]
            {
                name, Txt(Label(f.Load), 13), Txt($"{(r.Unit == "bytes" ? f.Bytes : f.Chars):N0}", 13), Txt($"{f.Tokens:N0}", 13, false, Muted),
                Txt(counted ? $"{running:N0}" : "", 13, false, r.Limit is { } l && running > l && counted ? ErrorKey : Muted), Txt(f.Note ?? "", 12, false, Muted)
            }, () => { _preview = _preview == f.Path ? null : f.Path; if (_last != null) _host.Content = Show(_last); }, _preview == f.Path);
        }).ToList();
        sp.Children.Add(Table.Build(new[] { new Col("File", "2.4*"), new Col("Loads", "1.1*"), new Col(r.Unit == "bytes" ? "Bytes" : "Characters", ".8*", true),
            new Col("≈Tokens", ".7*", true), new Col("Running total", ".9*", true), new Col("Note", "1.8*") }, rows));
        if (_preview != null && r.Files.Any(f => f.Path == _preview)) sp.Children.Add(Preview(_preview));
        return sp;
    }

    static string Label(LoadWhen w) => w switch
    {
        LoadWhen.Always => "At launch", LoadWhen.Imported => "At launch (import)", LoadWhen.OnDemand => "On demand", LoadWhen.Conditional => "Matching files",
        LoadWhen.AgentDecides => "Agent decides", LoadWhen.Manual => "When mentioned", _ => "Ignored"
    };

    /// <summary>Use of the limit: accent up to 75 %, warning to 100 %, error over it.</summary>
    static Control Bar(long used, long limit, ContextReport r)
    {
        double pct = limit == 0 ? 0 : 100.0 * used / limit;
        var g = new Grid { Height = 8, ColumnDefinitions = new ColumnDefinitions($"{Math.Max(Math.Min(pct, 100), 0.01)}*,{Math.Max(100 - pct, 0.01)}*") };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(4) }.Themed(Border.BackgroundProperty, pct > 100 ? ErrorKey : pct >= 75 ? WarningKey : Accent));
        g.Children.Add(new Border { CornerRadius = new CornerRadius(4) }.Themed(Border.BackgroundProperty, Subtle).With(b => Grid.SetColumn(b, 1)));
        return new StackPanel { Spacing = 4, Children = { g, Txt($"{pct:0}% of {limit:N0} {r.Unit}. {r.LimitNote}", 12, false, Muted) } };
    }

    string Show(string path) =>
        _project != null && ContextBudget.Inside(path, _project) ? Path.GetRelativePath(_project, path)
        : ContextBudget.Inside(path, PathUtil.Home) ? "~" + Path.DirectorySeparatorChar + Path.GetRelativePath(PathUtil.Home, path) : path;

    /// <summary>The clicked file, rendered; its links open in the same preview.</summary>
    Control Preview(string path)
    {
        var view = FileView.Show(path, false, link =>
        {
            if (SkillDoc.IsExternal(link)) { PathUtil.OpenFile(link); return; }
            try
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", Uri.UnescapeDataString(link.Split('#')[0])));
                if (File.Exists(target)) { _preview = target; if (_last != null) _host.Content = Show(_last); }
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        }, out bool scrolls);
        Control body = scrolls ? new ScrollViewer { Content = view, MaxHeight = 420 } : view.With(v => v.Height = 380);
        return new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = new StackPanel { Spacing = 8, Children =
        {
            Row(Txt(Show(path), 13, true).With(t => t.FontFamily = Ui.Theme.Mono), IconBtn("open", "Open file", () => PathUtil.OpenFile(path)), IconBtn("copy", "Copy path", () => Toast.Copy(x.Owner, path))),
            body
        } } }.Themed(Border.BackgroundProperty, Surface);
    }
}
