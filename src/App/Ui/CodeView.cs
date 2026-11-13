using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace AiStackprint.Ui;

/// <summary>
/// Text area with syntax highlighting (AvaloniaEdit + TextMate grammars, both MIT).
/// The only file that touches AvaloniaEdit; the rest of the UI uses this class.
/// </summary>
class CodeView : Border
{
    static RegistryOptions? _registry;
    static SharedGrammars? _shared;
    static RegistryOptions Registry => _registry ??= new RegistryOptions(ThemeName.LightPlus);
    /// <summary>Every view's highlighter reads grammars through this, so each grammar is parsed once, not once per view (Markdown and shell embed dozens of languages: ~15 MB each).</summary>
    static SharedGrammars Shared => _shared ??= new SharedGrammars(Registry);
    static int _live;   // installed highlighters; the grammars are dropped when the last one goes
    bool _disposed;

    /// <summary>
    /// true = keep highlighting while detached until the owner calls <see cref="Dispose"/> (the log viewer's raw view, which is re-attached on every page rebuild).
    /// Default: the highlighter and its grammars are released when the view leaves the screen and set up again if it comes back.
    /// </summary>
    public bool KeepOnDetach { get; init; }

    readonly TextEditor _editor;
    readonly string? _scope;
    TextMate.Installation? _textMate;   // only while on screen: a highlighter holds its own compiled copy of the grammar (~15 MB for Markdown or shell)

    /// <summary>AvaloniaEdit's control templates; add once to Application.Styles.</summary>
    public static IStyle EditorStyles() => new StyleInclude(new Uri("avares://AiStackprint/")) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") };

    /// <param name="path">File name or extension (".cs") that picks the grammar.</param>
    /// <param name="lineNumbers">Show line numbers (padded from the edge).</param>
    /// <param name="compact">Code block inside other content: no inner scrolling vertically, grows to fit up to MaxHeight.</param>
    public CodeView(string path, bool readOnly = true, bool lineNumbers = true, bool compact = false)
    {
        _editor = new TextEditor
        {
            IsReadOnly = readOnly, ShowLineNumbers = lineNumbers, WordWrap = false, FontFamily = Ui.Theme.Mono, FontSize = Ui.Theme.MonoSize, FontWeight = Ui.Theme.MonoWeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = compact ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            Padding = new Thickness(lineNumbers ? 0 : 8, 4, 4, 4)
        }.Themed(TextEditor.BackgroundProperty, W.Background).Themed(TextEditor.ForegroundProperty, W.Text);
        _editor.TextArea.Themed(TextArea.SelectionBrushProperty, W.SelectionKey);
        _editor.TextArea.SelectionForeground = null;   // keep syntax colors readable inside the selection
        if (lineNumbers)
            foreach (var m in _editor.TextArea.LeftMargins.OfType<LineNumberMargin>())
                m.Margin = new Thickness(10, 0, 8, 0);   // space from the edge and from the text
        if (compact) MaxHeight = 420;
        _scope = Language(path) is { } lang ? Registry.GetScopeByLanguageId(lang.Id) : null;
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        AttachedToVisualTree += (_, _) => Install();
        DetachedFromVisualTree += (_, _) => { if (!KeepOnDetach) Uninstall(); };
        Child = _editor;
    }

    /// <summary>Grammar for a file. TOML has no bundled grammar, so it uses INI; JSON lines use JSON; .txt logs use the log grammar.</summary>
    static Language? Language(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant() switch { ".toml" => ".ini", ".jsonl" => ".json", ".txt" => ".log", "" => ".md", var e => e };
        return Registry.GetLanguageByExtension(ext);
    }

    void ApplyTheme() => _textMate?.SetTheme(Registry.LoadTheme(ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus));

    void Install()
    {
        if (_disposed || _textMate != null) return;
        _textMate = _editor.InstallTextMate(Shared);
        if (_scope != null) _textMate.SetGrammar(_scope);
        _live++;
        ApplyTheme();
    }

    void Uninstall()
    {
        if (_textMate == null) return;
        _textMate.Dispose(); _textMate = null;
        if (--_live <= 0) { _live = 0; _registry = null; _shared = null; MemoryTrim.Request(); }   // the grammars are the largest thing the app loads
    }

    /// <summary>Releases the highlighter for good and, with the last view, the loaded grammars. Safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flashTimer?.Stop();
        Uninstall();
    }

    /// <summary>Grammar source shared by all views: parsed grammars are kept and handed to each highlighter (TextMate copies before compiling, so sharing is safe).</summary>
    sealed class SharedGrammars(RegistryOptions inner) : TextMateSharp.Registry.IRegistryOptions
    {
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, TextMateSharp.Internal.Types.IRawGrammar> _grammars = new();
        public TextMateSharp.Internal.Types.IRawGrammar GetGrammar(string scopeName) => _grammars.GetOrAdd(scopeName, inner.GetGrammar);
        public TextMateSharp.Themes.IRawTheme GetTheme(string scopeName) => inner.GetTheme(scopeName);
        public ICollection<string> GetInjections(string scopeName) => inner.GetInjections(scopeName);
        public TextMateSharp.Themes.IRawTheme GetDefaultTheme() => inner.GetDefaultTheme();
    }

    Flasher? _flash;
    Avalonia.Threading.DispatcherTimer? _flashTimer;

    /// <summary>Tints lines from <paramref name="fromLine"/> (1-based) to the end and fades the tint out over <paramref name="seconds"/>.</summary>
    public void Flash(int fromLine, double seconds)
    {
        if (seconds <= 0 || _disposed) return;
        var tv = _editor.TextArea.TextView;
        if (_flash == null) { _flash = new Flasher(); tv.BackgroundRenderers.Add(_flash); }
        _flash.From = Math.Max(1, fromLine); _flash.Start = DateTime.UtcNow; _flash.Seconds = seconds;
        _flash.Brush = this.TryFindResource(W.HighlightKey, ActualThemeVariant, out var b) ? b as Avalonia.Media.IBrush : null;
        _flashTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(60), Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
        {
            tv.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
            if (_disposed || _flash!.Done) _flashTimer!.Stop();
        });
        _flashTimer.Start();
    }

    /// <summary>Draws the fading tint behind the flashed lines.</summary>
    sealed class Flasher : AvaloniaEdit.Rendering.IBackgroundRenderer
    {
        public int From; public DateTime Start; public double Seconds; public Avalonia.Media.IBrush? Brush;
        double Progress => (DateTime.UtcNow - Start).TotalSeconds / Seconds;
        public bool Done => Seconds <= 0 || Progress >= 1;
        public AvaloniaEdit.Rendering.KnownLayer Layer => AvaloniaEdit.Rendering.KnownLayer.Background;

        public void Draw(AvaloniaEdit.Rendering.TextView tv, Avalonia.Media.DrawingContext dc)
        {
            if (Brush == null || Done || !tv.VisualLinesValid) return;
            using (dc.PushOpacity(Math.Clamp(1 - Progress, 0, 1)))
                foreach (var vl in tv.VisualLines)
                    if (vl.FirstDocumentLine.LineNumber >= From)
                        dc.FillRectangle(Brush, new Rect(0, vl.VisualTop - tv.ScrollOffset.Y, tv.Bounds.Width, vl.Height));
        }
    }

    public string Text { get => _editor.Text; set => _editor.Text = value; }
    public bool ReadOnly { get => _editor.IsReadOnly; set => _editor.IsReadOnly = value; }
    public event EventHandler? Changed { add => _editor.TextChanged += value; remove => _editor.TextChanged -= value; }
    public void ScrollToEnd() => _editor.ScrollToEnd();

    /// <summary>Scrolls to a 1-based line and selects <paramref name="length"/> characters from <paramref name="column"/> (0-based).</summary>
    public void Reveal(int line, int column, int length)
    {
        var doc = _editor.Document;
        if (doc == null || doc.LineCount == 0) return;
        var l = doc.GetLineByNumber(Math.Clamp(line, 1, doc.LineCount));
        int start = l.Offset + Math.Clamp(column, 0, l.Length);
        _editor.Select(start, Math.Clamp(length, 0, l.Offset + l.Length - start));
        _editor.ScrollTo(l.LineNumber, column);
    }
}
