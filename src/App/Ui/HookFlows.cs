// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace AiStackprint.Ui;

/// <summary>A piece of a hook flow diagram.</summary>
#pragma warning disable S2094 // a closed set of cases (step, tool, box) matched with a switch: the base has nothing to hold
internal abstract record FlowNode;
#pragma warning restore S2094

/// <summary>One card: the events it holds and what happens there (the tooltip).</summary>
internal sealed record FlowStep(string[] Events, string Hint) : FlowNode;

/// <summary>Where the tool itself runs: not an event.</summary>
internal sealed record FlowTool : FlowNode;

/// <summary>A labelled box around nodes; <paramref name="Strong"/> uses the accent color, else the muted one; <paramref name="Arrows"/> puts an arrow between its nodes.</summary>
internal sealed record FlowBox(string Label, bool Strong, bool Arrows, params FlowNode[] Nodes) : FlowNode;

/// <summary>The hook events of one app: the main order, and the events outside it (each with when it fires).</summary>
/// <param name="AppIds">The apps whose hooks this flow describes (hooks of these apps get the "yours" label).</param>
/// <param name="Note">Said under the diagram: what the app's documentation leaves open.</param>
internal sealed record HookFlowDef(string Label, string[] AppIds, FlowNode[] Main, (string Event, string When)[] Other, string? Note = null);

/// <summary>The hook event diagrams of the apps that have hooks, from each app's own documentation (checked Nov 2026).</summary>
internal static class HookFlows
{
    private const string Unordered = "The documentation lists these events but not the order they fire in; the boxes group them by what they do.";

    private static FlowStep S(string hint, params string[] events) => new(events, hint);

    private static readonly HookFlowDef[] _defs =
    [
        new("Claude Code", ["claude-code"],
        [
            S("A session begins or resumes", "SessionStart"),
            new FlowBox("Each turn", true, true,
                S("You send a prompt", "UserPromptSubmit"),
                new FlowBox("Agentic loop, once per tool call", false, false,
                    S("Before the tool runs; can block it", "PreToolUse"),
                    S("A permission dialog would show", "PermissionRequest"),
                    new FlowTool(),
                    S("The tool ran, or failed", "PostToolUse", "PostToolUseFailure"),
                    S("Every parallel call is done", "PostToolBatch"),
                    S("A sub-agent starts or stops", "SubagentStart", "SubagentStop"),
                    S("A task is made or finished", "TaskCreated", "TaskCompleted")),
                S("The agent finishes its answer; the next prompt starts a new turn", "Stop", "StopFailure")),
            S("A teammate idles, the context is compacted", "TeammateIdle", "PreCompact", "PostCompact"),
            S("The session closes", "SessionEnd")
        ],
        [
            ("Setup", "Opt-in start"), ("UserPromptExpansion", "A slash command expands"), ("PermissionDenied", "Auto mode denied a call"),
            ("Elicitation", "An MCP server asks you"), ("ElicitationResult", "You answered it"), ("Notification", "Async"), ("ConfigChange", "Async"),
            ("PreModelSwitch", "Sequential"), ("PostModelSwitch", "Async"), ("WorktreeCreate", "Isolation"), ("WorktreeRemove", "Teardown"),
            ("CwdChanged", "The folder changed"), ("FileChanged", "A watched file changed"), ("DirectoryAdded", "A folder was added"),
            ("InstructionsLoaded", "Async"), ("MessageDisplay", "Display")
        ]),

        new("Gemini CLI", ["gemini-cli"],
        [
            S("Startup, resume or clear; can add context", "SessionStart"),
            new FlowBox("Each turn", true, true,
                S("After you submit a prompt, before planning; can block the turn", "BeforeAgent"),
                new FlowBox("Per model call", false, false,
                    S("Before the request; can change it or answer in its place", "BeforeModel"),
                    S("Before the model picks tools; can filter them", "BeforeToolSelection"),
                    S("After each response chunk; can replace it", "AfterModel")),
                new FlowBox("Per tool call", false, false,
                    S("Before the tool runs; can block or rewrite it", "BeforeTool"),
                    new FlowTool(),
                    S("After it ran; can replace the result", "AfterTool")),
                S("The final response; can reject it and force a retry", "AfterAgent")),
            S("Exit or clear; best effort, cannot block", "SessionEnd")
        ],
        [("PreCompress", "Before the history is summarized"), ("Notification", "A system alert, e.g. a tool permission")], Unordered),

        new("Cursor", ["cursor"],
        [
            S("A conversation begins", "sessionStart"),
            new FlowBox("Each turn", true, true,
                S("Before a prompt is sent; can block it", "beforeSubmitPrompt"),
                new FlowBox("Agentic loop", false, false,
                    S("Before any tool call; can deny or change it", "preToolUse"),
                    S("Before and after a shell command", "beforeShellExecution", "afterShellExecution"),
                    S("Before and after an MCP tool", "beforeMCPExecution", "afterMCPExecution"),
                    S("Before the agent reads a file; can deny", "beforeReadFile"),
                    new FlowTool(),
                    S("After the agent edits a file", "afterFileEdit"),
                    S("After a tool call, or when it fails", "postToolUse", "postToolUseFailure"),
                    S("A sub-agent starts or stops", "subagentStart", "subagentStop"),
                    S("After each thought and response", "afterAgentThought", "afterAgentResponse")),
                S("The agent loop ends", "stop")),
            S("The conversation ends", "sessionEnd")
        ],
        [
            ("preCompact", "Before the context is compacted"), ("workspaceOpen", "A workspace opens or its folders change"),
            ("beforeTabFileRead", "Tab (inline completion) reads a file"), ("afterTabFileEdit", "Tab edited a file")
        ], Unordered),

        new("Copilot", ["github-copilot"],
        [
            S("A session begins or resumes", "sessionStart"),
            new FlowBox("Each turn", true, true,
                S("You submit a prompt", "userPromptSubmitted"),
                S("The prompt was transformed for the model; can rewrite it", "userPromptTransformed"),
                new FlowBox("Agentic loop", false, false,
                    S("Before each tool; can allow, deny or change it", "preToolUse"),
                    S("Before the permission check; can allow or deny (CLI)", "permissionRequest"),
                    new FlowTool(),
                    S("After a tool, or when it fails", "postToolUse", "postToolUseFailure"),
                    S("A sub-agent starts or stops; stopping can be forced to continue", "subagentStart", "subagentStop")),
                S("The main agent finishes; can force another turn", "agentStop")),
            S("The session ends", "sessionEnd")
        ],
        [("errorOccurred", "An error occurs"), ("notification", "A system notification (CLI)"), ("preCompact", "Before the context is compacted")], Unordered),

        new("Antigravity", ["antigravity-cli", "antigravity"],
        [
            S("A new session starts, before the first model call", "SessionStart"),
            new FlowBox("Agent loop", true, false,
                S("Before each model call; can stop the loop", "PreInvocation"),
                S("Before a tool runs; can allow, deny, ask or change it", "PreToolUse"),
                new FlowTool(),
                S("After a tool completes", "PostToolUse"),
                S("After each model call; can force the loop to go on or stop", "PostInvocation")),
            S("The loop ends; can keep the agent running", "Stop")
        ], [], Unordered),

        new("Kiro", ["kiro"],
        [
            S("A new chat session starts", "SessionStart"),
            new FlowBox("Each turn", true, true,
                S("You submit a prompt (the IDE can block it)", "PromptSubmit"),
                new FlowBox("Agentic loop", false, false,
                    S("Before a tool call; can block or change it", "PreToolUse"),
                    S("Before a permission prompt; can allow or deny", "PermissionRequest"),
                    new FlowTool(),
                    S("After a tool call", "PostToolUse"),
                    S("A spec task begins or completes", "PreTaskExecution", "PostTaskExecution")),
                S("The agent finishes its turn", "AgentStop")),
            S("A session is torn down (CLI)", "SessionEnd")
        ],
        [
            ("PreCompact", "Before compaction; can cancel it"), ("PostCompact", "After compaction"),
            ("FileCreate", "The agent creates a file"), ("FileSave", "The agent saves a file"), ("FileDelete", "The agent deletes a file"),
            ("AgentSpawn", "Older name of session start (CLI)"), ("Manual", "On demand")
        ], Unordered)
    ];

    /// <summary>Every app's flow, by app name A-Z (the order of the picker).</summary>
    public static readonly HookFlowDef[] All = [.. _defs.OrderBy(f => f.Label, StringComparer.OrdinalIgnoreCase)];   // after _defs: static fields start in the order written
}
