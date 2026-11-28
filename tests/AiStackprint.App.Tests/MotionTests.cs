// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AiStackprint.App.Tests;

/// <summary>Enter and leave motions: they start hidden, end shown, and a dialog that fades out still answers with what it was closed with.</summary>
public sealed class MotionTests : IDisposable
{
    public MotionTests()
    {
        Motion.Disabled = false;   // the other tests run without motion
        Motion.Enabled = true;     // another test may have applied settings with animations off
    }

    public void Dispose()
    {
        Motion.Disabled = true;
        Motion.Enabled = true;
    }

    /// <summary>Draws frames for at least <paramref name="ms"/> milliseconds of real time (motions run on the window's frames).</summary>
    private static void Play(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        do
        {
            Thread.Sleep(8);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        while (DateTime.UtcNow < end);
    }

    [AvaloniaFact]
    public void Entering_starts_hidden_and_ends_fully_shown_in_place()
    {
        var box = new Border { Width = 40, Height = 40 };
        using var w = new WindowCloser(Ui.Show(box));
        Motion.Enter(box, Duration.Fast, -Space.XS);
        Assert.Equal(0, box.Opacity);   // hidden at once, before the first frame
        Play(Duration.Fast + 100);
        Assert.Equal(1, box.Opacity);
        Assert.Null(box.RenderTransform);   // nothing is left behind once it ends
    }

    [AvaloniaFact]
    public void A_panel_shown_again_after_it_left_is_not_left_hidden()
    {
        var box = new Border { Width = 40, Height = 40 };
        using var w = new WindowCloser(Ui.Show(box));
        var gone = Motion.Leave(box, Duration.Faster);
        Play(Duration.Faster + 100);
        Assert.True(gone.IsCompleted);
        Assert.Equal(0, box.Opacity);   // stays hidden after leaving
        Motion.Enter(box, Duration.Faster);
        Play(Duration.Faster + 100);
        Assert.Equal(1, box.Opacity);
    }

    [AvaloniaFact]
    public void A_pop_up_fades_in_as_it_opens()
    {
        var button = new Button { Content = "Info" };
        var help = new TextBlock { Text = "Help" };
        var flyout = new Flyout { Content = help };
        button.Flyout = flyout;
        using var w = new WindowCloser(Ui.Show(button));
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        var presenter = help.GetVisualAncestors().OfType<FlyoutPresenter>().First();
        Assert.True(presenter.Opacity < 1);
        Play(Duration.Fast + 100);
        Assert.Equal(1, presenter.Opacity);
        Assert.Null(presenter.RenderTransform);
        flyout.Hide();
    }

    [AvaloniaFact]
    public void With_animations_off_nothing_moves()
    {
        Motion.Enabled = false;
        var box = new Border { Width = 40, Height = 40 };
        using var w = new WindowCloser(Ui.Show(box));
        Motion.Enter(box, Duration.Slow, Space.XL);
        Assert.Equal(1, box.Opacity);
        Assert.True(Motion.Leave(box, Duration.Slow).IsCompleted);
    }

    [AvaloniaFact]
    public void A_dialog_fades_out_and_still_answers_with_the_result_it_was_closed_with()
    {
        var owner = Ui.Show(new Panel());
        var dialog = new Window();
        Dialogs.Chrome(dialog, "Remove?", new TextBlock { Text = "Sure?" }, Widths.DialogS);
        var answer = dialog.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        dialog.Close(true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.IsVisible);   // held while it fades
        Play(Duration.Fast + 100);
        Assert.True(answer.IsCompleted);
        Assert.True(answer.Result);
        owner.Close();
    }

    private static (StackPanel Group, Button Chosen) Choices(string chosen)
    {
        var group = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        Button? on = null;
        foreach (var v in new[] { "one", "two", "three" })
        {
            var b = new Button { Content = new TextBlock { Text = v } };
            if (v == chosen)
            {
                Motion.Glide(b, nameof(Choices), v);
                on = b;
            }

            group.Children.Add(b);
        }

        return (group, on!);
    }

    [AvaloniaFact]
    public void A_new_choice_glides_its_fill_from_the_last_one_while_its_text_stays_put()
    {
        var (first, _) = Choices("one");
        var w = Ui.Show(first);
        Play(16);
        var (next, chosen) = Choices("three");   // rebuilt with another choice, like the theme switch
        w.Content = next;
        Play(16);
        var text = (TextBlock)chosen.Content!;
        var laid = new Avalonia.Point(chosen.Bounds.X + text.Bounds.X, chosen.Bounds.Y + text.Bounds.Y);
        Assert.IsType<Avalonia.Media.MatrixTransform>(chosen.RenderTransform);   // the fill starts where "one" was
        Assert.True(chosen.RenderTransform.Value.M31 < 0);
        var shown = text.TranslatePoint(default, next)!.Value;
        Assert.Equal(laid.X, shown.X, 0.5);   // the text is where it belongs all along
        Assert.Equal(laid.Y, shown.Y, 0.5);
        Play(Duration.Normal + 100);
        Assert.Null(chosen.RenderTransform);
        Assert.Null(text.RenderTransform);
        w.Close();
    }

    [AvaloniaFact]
    public void A_stepped_number_comes_in_from_the_side_it_moved_to()
    {
        var box = W.Stepper(new NumericUpDown { Minimum = 0, Maximum = 10, Value = 5, Increment = 1 }, "00");
        var w = Ui.Show(box);
        var plus = box.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "Increase");
        plus.Command = null;
        plus.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var number = box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().First();
        Assert.Equal(6, box.Value);
        Assert.True(number.Opacity < 1);
        Play(Duration.Faster + 100);
        Assert.Equal(1, number.Opacity);
        w.Close();
    }

    [AvaloniaFact]
    public void A_group_chevron_turns_only_when_its_group_was_just_opened_or_closed()
    {
        var still = Motion.Chevron(new Border(), "a", false);
        Assert.Equal(-90, ((Avalonia.Media.RotateTransform)still.RenderTransform!).Angle);
        Motion.Turn("b");
        var opened = Motion.Chevron(new Border { Width = 8, Height = 8 }, "b", true);
        Assert.Equal(-90, ((Avalonia.Media.RotateTransform)opened.RenderTransform!).Angle);   // starts where it was, before the first frame
        var w = Ui.Show(opened);
        Play(Duration.Fast + 100);
        Assert.Equal(0, ((Avalonia.Media.RotateTransform)opened.RenderTransform).Angle);
        w.Close();
    }

    [AvaloniaFact]
    public void A_row_eases_to_its_hover_color_only_with_animations_on()
    {
        Assert.NotNull(new Border().Hover().Transitions);
        Motion.Enabled = false;
        Assert.Null(new Border().Hover().Transitions);
    }

    [AvaloniaFact]
    public void Bars_grow_the_first_time_their_numbers_show_and_stay_still_after()
    {
        var data = "test:" + Guid.NewGuid();
        var bar = Motion.Grow(new Border { Width = 100, Height = 10 }, data);
        var w = Ui.Show(bar);
        Assert.Equal(0, ((Avalonia.Media.ScaleTransform)bar.RenderTransform!).ScaleX);   // from nothing, at the left
        Play(Duration.Slow + 100);
        Assert.Null(bar.RenderTransform);
        Assert.Null(Motion.Grow(new Border(), data).RenderTransform);   // the same numbers again: still
        w.Close();
    }

    [AvaloniaFact]
    public void A_number_tile_counts_from_its_last_number_and_ends_on_its_own_text()
    {
        var key = "tile:" + Guid.NewGuid();
        var text = Motion.CountUp(new TextBlock { Text = "1,200" }, key, 1200);
        var w = Ui.Show(text);
        Play(48);
        Assert.NotEqual("1,200", text.Text);   // on its way up from 0
        Play(Duration.Slow + 100);
        Assert.Equal("1,200", text.Text);
        Assert.Equal("1,200", Motion.CountUp(new TextBlock { Text = "1,200" }, key, 1200).Text);   // unchanged: no counting
        w.Close();
    }

    [AvaloniaFact]
    public void A_removed_row_closes_up_before_it_goes()
    {
        var row = new Border { Height = 30 };
        var list = new StackPanel { Children = { row, new Border { Height = 30 } } };
        var w = Ui.Show(list);
        var gone = Motion.Remove(row);
        Play(80);
        Assert.False(gone.IsCompleted);
        Assert.True(row.Height < 30);
        Play(Duration.Fast + 100);
        Assert.True(gone.IsCompleted);
        w.Close();
    }

    [AvaloniaFact]
    public void A_near_step_glides_into_view_and_a_far_one_is_jumped_to()
    {
        var rows = new StackPanel();
        for (var i = 0; i < 200; i++)
        {
            rows.Children.Add(new Border { Height = 40 });
        }

        var sv = new ScrollViewer { Height = 400, Content = rows };
        var w = Ui.Show(sv);
        Motion.ScrollIntoView(rows.Children[12]);   // just below the view
        Assert.Equal(0, sv.Offset.Y);   // starts where it was
        Play(Duration.Gentle + 100);
        Assert.True(sv.Offset.Y > 0);
        Motion.ScrollIntoView(rows.Children[190]);   // far: at once
        Assert.True(sv.Offset.Y > 7000);
        w.Close();
    }

    private sealed class WindowCloser(Window w) : IDisposable
    {
        public void Dispose() => w.Close();
    }
}
