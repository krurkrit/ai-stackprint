// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Ui;

/// <summary>The kinds of app, in one order, for the Category help (Apps page) and the Add an app dialog, so both list the same ones.</summary>
internal static class AppCategories
{
    public static readonly string[] Order = ["agent", "editor", "chat", "orchestrator", "runtime", "shared", "custom"];

    /// <summary>One short line per category.</summary>
    public static readonly Dictionary<string, string> About = new()
    {
        ["agent"] = "Works on your files and runs tools, like Claude Code or Codex.",
        ["editor"] = "A code editor or IDE with AI built in.",
        ["chat"] = "A chat app for talking to a model.",
        ["orchestrator"] = "Runs several agents at once.",
        ["runtime"] = "Runs or fetches models, like Ollama or LM Studio.",
        ["shared"] = "Folders several apps read, like shared skills.",
        ["custom"] = "An app you added yourself."
    };
}
