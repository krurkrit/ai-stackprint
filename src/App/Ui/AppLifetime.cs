// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace AiStackprint.Ui;

/// <summary>What <see cref="AppHost"/> needs of the desktop lifetime (Avalonia's own interface cannot be implemented, so tests give a fake of this one).</summary>
internal interface IAppLifetime
{
    ShutdownMode ShutdownMode { get; set; }
    Window? MainWindow { get; set; }
    void Shutdown();
}

/// <summary>The real desktop lifetime.</summary>
internal sealed class DesktopLifetime(IClassicDesktopStyleApplicationLifetime d) : IAppLifetime
{
    public ShutdownMode ShutdownMode { get => d.ShutdownMode; set => d.ShutdownMode = value; }
    public Window? MainWindow { get => d.MainWindow; set => d.MainWindow = value; }

    public void Shutdown() => d.Shutdown();
}
