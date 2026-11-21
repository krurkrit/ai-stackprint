// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;

using AiStackprint.Services;

namespace AiStackprint.Core.Tests;

/// <summary>Every Core test runs on a made-up empty home, never this machine's: caches and data files the services write go there (docs/testing.md).</summary>
internal static class TestHome
{
    internal static readonly string Root = Path.Combine(Path.GetTempPath(), "sp-core-tests-home");

    [ModuleInitializer]
    internal static void Init() => PathUtil.UseHome(Root);
}
