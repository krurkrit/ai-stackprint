// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Avalonia.Controls;

using static AiStackprint.Ui.W;

namespace AiStackprint.Ui;

/// <summary>Settings › Updates › Model data: the data in use, the last check and Check now. The download is <see cref="ModelDataUpdate"/> in Core.</summary>
internal static partial class UpdateView
{
    private static Control ModelData(Ctx x)
    {
        var status = new ContentControl { Content = ModelDataStatus(x) };
        var result = new ContentControl();
        return new StackPanel
        {
            Spacing = Space.M,
            Children =
            {
                Title("Model data", TextSize.Body),
                status,
                Row(Btn("Check model data now", async () =>
                {
                    result.Content = Busy.Loading("Checking…", 14);
                    var r = await FetchModelData(x);
                    status.Content = ModelDataStatus(x);
                    result.Content = r.Ok
                        ? Txt(r.Message, TextSize.Small, false, Muted)
                        : Row(Icon("warning", IconSize.S).Themed(Avalonia.Controls.Documents.TextElement.ForegroundProperty, WarningMark), Txt(r.Message, TextSize.Small, false, Muted));
                }, false, "arrow_sync"), result)
            }
        };
    }

    private static TextBlock ModelDataStatus(Ctx x)
    {
        var t = ModelCatalog.Load();
        var data = $"In use: data from {t.Updated ?? "an unknown date"} ({(t.Downloaded ? "downloaded" : "built into this version")}), {t.Models.Count:N0} models.";
        var last = x.S.State.ModelDataCheckedUtc is { } at ? $" Last check {at.ToLocalTime():yyyy-MM-dd HH:mm}: {x.S.State.ModelDataResult}" : " Not checked yet.";
        return Txt(data + last, TextSize.Small, false, Muted);
    }

    /// <summary>One check; remembers when, what it found and the ETag of a saved copy. A failure keeps the copy in use.</summary>
    private static async Task<ModelDataResult> FetchModelData(Ctx x)
    {
        var st = x.S.State;
        var r = await ModelDataUpdate.RunAsync(st.ModelDataEtag);
        st.ModelDataCheckedUtc = DateTime.UtcNow;
        st.ModelDataResult = r.Message;
        if (r.Saved)
        {
            st.ModelDataEtag = r.Etag;
        }

        StateStore.Save(st);
        return r;
    }
}
