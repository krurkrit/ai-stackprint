// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>What a reply cost, under the step that starts it: the request's tokens (the cached part), the reply's tokens and the estimated price.</summary>
internal partial class ReadableLog
{
    private static Control WithUsage(Control step, UsageEvent u)
    {
        var parts = new List<string> { $"{UsageCard.Fmt(u.RequestInput)} in" + (u.CacheRead > 0 ? $" ({UsageCard.Fmt(u.CacheRead)} cached)" : ""), $"{UsageCard.Fmt(u.Output)} out" };
        if (ModelCatalog.Cost(u) is { } cost)
        {
            parts.Add(StepMoney(cost));
        }

        var caption = Txt(u.Model + " · " + string.Join(" · ", parts), TextSize.Caption, false, Muted).With(t =>
        {
            t.HorizontalAlignment = HorizontalAlignment.Right;
            t.Margin = new Thickness(0, Space.XXS, 0, 0);
        });
        ToolTip.SetTip(caption, "Tokens of this reply's request and output, and its estimated cost at the model's list price (the Usage page's figures).");
        return new StackPanel { Children = { step, caption } };
    }

    /// <summary>"$0.0123" under ten cents, else like the Usage page.</summary>
    internal static string StepMoney(decimal d) => d < 0.1m ? "$" + d.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) : UsageCard.Money(d);
}
