// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

namespace AiStackprint.Ui;

/// <summary>Sends limit alerts to the local system's notifications. Nothing is sent anywhere else. Tests replace <see cref="Notifier.Current"/> with a fake.</summary>
internal interface INotifier
{
    /// <summary>Registers (or removes) what the system needs to show and route the app's notifications: on Windows the app id and the <c>aistackprint:</c> link.</summary>
    void Register(bool on);
    void Show(Alert a);
}

/// <summary>
/// Windows: a toast (Action Center) whose buttons open <c>aistackprint:</c> links, which start the app and reach the running copy through <see cref="SingleInstance"/>.
/// macOS and Linux, and Windows when a toast cannot be shown: <see cref="AlertPopup"/>, our own small window at the bottom right.
/// </summary>
internal static partial class Notifier
{
    public static INotifier Current { get; set; } = OperatingSystem.IsWindows() ? new WindowsToast(new PopupNotifier()) : new PopupNotifier();

    /// <summary>The link a button opens: the app's page, or pausing alerts.</summary>
    public static string OpenLink(string appId) => $"{SingleInstance.Protocol}:open/App:{Uri.EscapeDataString(appId)}";

    public const string PauseLink = SingleInstance.Protocol + ":pause";

    /// <summary>Our own pop-up: used where there is no system notifier we can reach.</summary>
    internal sealed class PopupNotifier : INotifier
    {
        public void Register(bool on) { }

        public void Show(Alert a) => AlertPopup.Show(a, m => Message?.Invoke(m));
    }

    /// <summary>Where a pop-up's buttons go: <see cref="AppHost.Message"/>.</summary>
    public static Action<string>? Message { get; set; }
}
