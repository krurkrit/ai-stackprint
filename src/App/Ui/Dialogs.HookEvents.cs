// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using Avalonia.Controls;

namespace AiStackprint.Ui;

internal static partial class Dialogs
{
    /// <summary>The hook events of an app as a diagram (the Event column's ⓘ on the Hooks page), whole, in a dialog of the design system.</summary>
    public static Task HookEvents(Window owner, IReadOnlyList<Hook> hooks) => HookEventsWindow(hooks).ShowDialog(owner);

    internal static Window HookEventsWindow(IReadOnlyList<Hook> hooks)
    {
        var w = new Window();
        Chrome(w, "Hook events", new HookFlow(hooks), Widths.DialogXL);   // no button: the × and Esc close it
        return w;
    }
}
