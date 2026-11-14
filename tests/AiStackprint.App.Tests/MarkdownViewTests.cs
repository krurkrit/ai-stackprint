// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Ui;

using Avalonia.Headless.XUnit;

using Xunit;

namespace AiStackprint.App.Tests;

public class MarkdownViewTests
{
    [AvaloniaFact]
    public void A_readme_that_mixes_html_and_markdown_shows_no_tags()
    {
        var view = MarkdownView.Document("""
            <p align="center"><img src="logo.svg" alt="Acme logo"></p>
            <h2>Install</h2>
            Run <code>npm i acme</code> &mdash; then <b>restart</b>.<br>
            <details><summary>Options</summary>

            - **fast**: on

            </details>

            ```html
            <div>kept in code</div>
            ```
            """);
        Ui.Show(view);
        var texts = Ui.Texts(view);

        Assert.Contains("Acme logo", texts);
        Assert.Contains("Install", texts);
        Assert.Contains("Run npm i acme — then restart.", texts);
        Assert.Contains("Options", texts);
        Assert.DoesNotContain(texts, t => t.Contains("<p") || t.Contains("<b>") || t.Contains("&mdash;") || t.Contains("**"));
    }
}
