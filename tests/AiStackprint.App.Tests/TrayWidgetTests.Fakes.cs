// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;
using AiStackprint.Ui;

using Avalonia.Controls;

namespace AiStackprint.App.Tests;

/// <summary>The fakes of <see cref="TrayWidgetTests"/>: the login entry, the notifier and the desktop lifetime.</summary>
public sealed partial class TrayWidgetTests
{
    private sealed class FakeLogin : ILoginItem
    {
        public bool IsOn { get; private set; }

        public void Set(bool on, string exe) => IsOn = on;
    }

    private sealed class FakeNotifier : INotifier
    {
        public List<Alert> Sent { get; } = [];
        public bool Registered { get; private set; }

        public void Register(bool on) => Registered = on;

        public void Show(Alert a) => Sent.Add(a);
    }

    /// <summary>A desktop lifetime that records shutdowns instead of ending the test run.</summary>
    private sealed class FakeLifetime : IAppLifetime
    {
        public int Shutdowns { get; private set; }
        public ShutdownMode ShutdownMode { get; set; }
        public Window? MainWindow { get; set; }

        public void Shutdown() => Shutdowns++;
    }
}
