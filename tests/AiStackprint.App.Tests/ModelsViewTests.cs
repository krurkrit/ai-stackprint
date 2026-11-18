// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Nodes;

using AiStackprint.Models;
using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

public sealed class ModelsViewTests : IDisposable
{
    private static readonly JsonObject _data = JsonNode.Parse("""
        {
          "claude-opus-5-5": { "provider": "anthropic", "contextWindow": 1000000, "maxOutput": 128000, "price": { "input": 4, "output": 20 } },
          "gpt-6": { "provider": "openai", "contextWindow": 400000, "price": { "input": 2, "output": 8 } }
        }
        """)!.AsObject();

    private readonly string _dir = Directory.CreateTempSubdirectory("modelsview-").FullName;
    private string File_ => Path.Combine(_dir, "models.json");
    private ModelEdits Edits => new(() => ModelCatalog.Rows(_data, File_), (k, v) => ModelCatalog.Save(_data, File_, k, v), (k, f) => ModelCatalog.Reset(File_, k, f));

    public void Dispose() => Directory.Delete(_dir, true);

    private (Window W, ModelsView View) Open(string? select = null, UiState? state = null)
    {
        var w = new Window { Width = 1400, Height = 900 };
        ModelsView? view = null;
        var x = new Ctx(new AppState { State = state ?? new UiState() }, w, _ => { }, () => { }) { Refresh = () => w.Content = view!.Build(select) };
        view = new ModelsView(x, Edits, _ => { });   // not the real state.json
        w.Content = view.Build(select);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return (w, view);
    }

    [AvaloniaFact]
    public void Models_are_listed_with_formatted_values_and_filtered_by_provider_and_name()
    {
        var (w, _) = Open();
        var texts = Ui.Texts(w);
        Assert.Contains("claude-opus-5-5", texts);
        Assert.Contains("gpt-6", texts);
        Assert.Contains("1M", texts);
        Assert.Contains("128K", texts);
        Assert.Contains("$4.00", texts);

        Ui.Click(Ui.Button(w, "openai 1"));
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("claude-opus-5-5", Ui.Texts(w));

        var (linked, _) = Open("gpt-6");   // from the Usage page: the search holds the model's name
        Assert.Equal("gpt-6", linked.GetVisualDescendants().OfType<TextBox>().Single().Text);
        Assert.DoesNotContain("claude-opus-5-5", Ui.Texts(linked));
        Assert.Contains("gpt-6", Ui.Texts(linked));
    }

    [AvaloniaFact]
    public void An_edited_value_is_saved_alone_marked_as_yours_and_reset_per_field()
    {
        var row = Edits.Rows().Single(r => r.Key == "gpt-6");
        var d = Dialogs.ModelEditor(row, null, Edits);
        d.Show();
        Dispatcher.UIThread.RunJobs();
        var boxes = d.GetVisualDescendants().OfType<TextBox>().ToList();
        Assert.Equal("2", boxes[2].Text);   // fields in order: context, max output, input…
        boxes[2].Text = "2.5";
        Ui.Click(Ui.Button(d, "Save"));
        Assert.Equal("""{"gpt-6":{"price":{"input":2.5}}}""", JsonNode.Parse(File.ReadAllText(File_))!["models"]!.ToJsonString());

        var (w, _) = Open();
        Assert.Contains("$2.50", Ui.Texts(w));

        var again = Dialogs.ModelEditor(Edits.Rows().Single(r => r.Key == "gpt-6"), null, Edits);
        again.Show();
        Dispatcher.UIThread.RunJobs();
        var reset = again.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) is string s && s.StartsWith("Reset", StringComparison.Ordinal));
        Ui.Click(reset);
        Ui.Click(Ui.Button(again, "Save"));
        Assert.Empty(Edits.Rows().Single(r => r.Key == "gpt-6").Changed);
    }

    [AvaloniaFact]
    public void A_model_is_added_with_its_prices_and_a_bad_value_is_refused()
    {
        var d = Dialogs.ModelEditor(null, "my-local", Edits);
        d.Show();
        Dispatcher.UIThread.RunJobs();
        var boxes = d.GetVisualDescendants().OfType<TextBox>().ToList();
        Assert.Equal("my-local", boxes[0].Text);
        boxes[3].Text = "abc";   // name, context, max output, input…
        Ui.Click(Ui.Button(d, "Save"));
        Assert.Contains(Ui.Texts(d), t => t.StartsWith("Input:", StringComparison.Ordinal));
        Assert.False(File.Exists(File_));

        boxes[3].Text = "0.5";
        boxes[4].Text = "1";
        Ui.Click(Ui.Button(d, "Save"));
        var added = Edits.Rows().Single(r => r.Key == "my-local");
        Assert.True(added.Added);
        Assert.Equal("0.5", added.Values["price.input"]);

        var (w, _) = Open("my-local");
        Assert.Contains("Yours", Ui.Texts(w));
        Assert.Contains("custom 1", w.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string));
    }

    [AvaloniaFact]
    public void Ticking_two_models_shows_the_radar_with_raw_values_in_tooltips()
    {
        var (w, _) = Open();
        Assert.DoesNotContain("Compare models", Ui.Texts(w));
        var boxes = w.GetVisualDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(2, boxes.Count);
        boxes[0].IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Tick 1 more to compare (up to 4).", Ui.Texts(w));

        w.GetVisualDescendants().OfType<CheckBox>().Skip(1).First().IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var texts = Ui.Texts(w);
        Assert.Contains("Compare models", texts);
        Assert.Contains("Cheap input", texts);
        Assert.Contains("Benchmarks", texts);   // the second chart; with benchmarks off it is a hint to turn them on
        Assert.Contains(texts, s => s.Contains("Settings › Benchmarks", StringComparison.Ordinal));
        var tips = w.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Select(e => ToolTip.GetTip(e)?.ToString() ?? "").ToList();
        Assert.Equal(12, tips.Count);   // two models, six axes
        Assert.Contains(tips, s => s.Contains("claude-opus-5-5") && s.Contains("Context: 1M") && s.Contains("100% on the scale of all models"));
        Assert.Contains(tips, s => s.Contains("gpt-6") && s.Contains("Cheap input: $2.00 per 1M") && s.Contains("95% on the scale of all models"));
        Assert.Contains(tips, s => s.Contains("gpt-6") && s.Contains("Max output: unknown"));
    }

    [AvaloniaFact]
    public void Ticked_models_are_kept_in_the_state_and_show_again_when_the_page_opens()
    {
        var state = new UiState();
        var (w, _) = Open(null, state);
        foreach (var cb in w.GetVisualDescendants().OfType<CheckBox>().ToList())
        {
            cb.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(["claude-opus-5-5", "gpt-6"], state.ComparedModels);

        var (again, _) = Open(null, new UiState { ComparedModels = [.. state.ComparedModels, "gone-model"] });   // a later visit; a model that no longer exists is dropped
        Assert.Contains("Compare models", Ui.Texts(again));
        Assert.All(again.GetVisualDescendants().OfType<CheckBox>(), cb => Assert.True(cb.IsChecked));

        Ui.Click(Ui.Button(again, "Clear"));
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("Compare models", Ui.Texts(again));
    }

    [AvaloniaFact]
    public void Retires_is_picked_as_a_date_saved_as_YYYY_MM_DD_and_cleared_with_the_clear_button()
    {
        var row = Edits.Rows().Single(r => r.Key == "gpt-6");
        var d = Dialogs.ModelEditor(row, null, Edits);
        d.Show();
        Dispatcher.UIThread.RunJobs();
        var picker = d.GetVisualDescendants().OfType<CalendarDatePicker>().Single();   // not a text box: nothing to type wrong
        Assert.Null(picker.SelectedDate);
        picker.SelectedDate = new DateTime(2027, 3, 4, 0, 0, 0, DateTimeKind.Unspecified);
        Ui.Click(Ui.Button(d, "Save"));
        Assert.Equal("2027-03-04", Edits.Rows().Single(r => r.Key == "gpt-6").Values["retires"]);

        var again = Dialogs.ModelEditor(Edits.Rows().Single(r => r.Key == "gpt-6"), null, Edits);
        again.Show();
        Dispatcher.UIThread.RunJobs();
        var shown = again.GetVisualDescendants().OfType<CalendarDatePicker>().Single();
        Assert.Equal(new DateTime(2027, 3, 4, 0, 0, 0, DateTimeKind.Unspecified), shown.SelectedDate);
        Ui.Click(again.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) is string s && s.StartsWith("Reset", StringComparison.Ordinal)));
        Assert.Null(shown.SelectedDate);   // reset: the model data has no date
    }
}
