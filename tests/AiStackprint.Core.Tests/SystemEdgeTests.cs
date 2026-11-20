// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

/// <summary>Opening files and folders on each operating system (nothing really opens: the launcher is replaced), running a program, and colored text.</summary>
[Collection("Environment")]
public sealed class SystemEdgeTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("aisys-").FullName;
    private readonly List<ProcessStartInfo> _started = [];
    private readonly Action<ProcessStartInfo> _before = PathUtil.Launcher;

    public SystemEdgeTests() => PathUtil.Launcher = _started.Add;

    public void Dispose()
    {
        PathUtil.Launcher = _before;
        PathUtil.OsOverride = null;
        Directory.Delete(_tmp, true);
    }

    [Theory]
    [InlineData("windows", "windows", true)]
    [InlineData("macos", "windows", false)]
    [InlineData("linux", "linux", true)]
    [InlineData("LINUX", "linux", true)]
    [InlineData("", "macos", true)]
    [InlineData(null, "macos", true)]
    [InlineData("plan9", "macos", false)]
    public void An_app_definition_applies_to_the_named_system_only(string? os, string current, bool expected)
    {
        PathUtil.OsOverride = current;
        Assert.Equal(expected, PathUtil.OsMatches(os));
    }

    [Fact]
    public void A_file_opens_in_its_default_app_through_the_shell_on_windows_and_with_a_tool_elsewhere()
    {
        var file = Path.Combine(_tmp, "a.txt");
        File.WriteAllText(file, "x");
        PathUtil.OsOverride = "windows";
        PathUtil.OpenFile(file);
        Assert.Equal((file, true), (_started[0].FileName, _started[0].UseShellExecute));

        PathUtil.OsOverride = "macos";
        PathUtil.OpenFile(file);
        Assert.Equal(("open", file), (_started[1].FileName, _started[1].ArgumentList.Single()));

        PathUtil.OsOverride = "linux";
        PathUtil.OpenFile(file);
        Assert.Equal(("xdg-open", file), (_started[2].FileName, _started[2].ArgumentList.Single()));
    }

    [Fact]
    public void A_folder_and_a_files_folder_open_in_the_file_manager()
    {
        var file = Path.Combine(_tmp, "b.txt");
        File.WriteAllText(file, "x");
        PathUtil.OsOverride = "windows";
        PathUtil.OpenFolder(file);
        Assert.EndsWith("explorer.exe", _started[0].FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"\"{_tmp}\"", _started[0].Arguments);

        PathUtil.OpenFile(_tmp);   // a folder given to OpenFile goes the same way
        Assert.EndsWith("explorer.exe", _started[1].FileName, StringComparison.OrdinalIgnoreCase);

        PathUtil.OsOverride = "macos";
        PathUtil.OpenFolder(_tmp);
        Assert.Equal(("open", _tmp), (_started[2].FileName, _started[2].ArgumentList.Single()));
        PathUtil.OsOverride = "linux";
        PathUtil.OpenFolder(_tmp);
        Assert.Equal("xdg-open", _started[3].FileName);
    }

    [Fact]
    public void A_system_with_nothing_to_open_it_with_is_not_an_error()
    {
        var tried = 0;
        PathUtil.Launcher = _ => { tried++; throw new System.ComponentModel.Win32Exception("no program"); };
        PathUtil.OpenFile(Path.Combine(_tmp, "none.txt"));
        PathUtil.Launcher = _ => { tried++; throw new InvalidOperationException("no process"); };
        PathUtil.OpenFolder(_tmp);
        Assert.Equal(2, tried);   // both were tried and neither raised
    }

    [Fact]
    public void Home_can_be_moved_and_tilde_follows_it()
    {
        PathUtil.UseHome(_tmp);
        try
        {
            Assert.Equal(_tmp, PathUtil.Home);
            Assert.Equal(Path.Combine(_tmp, "x", "y"), PathUtil.Expand("~/x/y"));
            Assert.Equal(Path.Combine(_tmp, ".ai-stackprint"), DataFolder.Root);
        }
        finally
        {
            PathUtil.UseHome(null);
        }

        Assert.NotEqual(_tmp, PathUtil.Home);
    }

    [Fact]
    public async Task A_program_on_the_path_runs_with_separate_arguments_and_a_missing_one_is_reported()
    {
        var (code, output) = await PluginManager.Run(["dotnet", "--version"], _tmp);
        Assert.Equal(0, code);
        Assert.Matches(@"^\d+\.\d+", output);
        await Assert.ThrowsAsync<FileNotFoundException>(() => PluginManager.Run(["definitely-not-a-program-ai"], null));
        Assert.Null(PluginManager.Which(Path.Combine(_tmp, "missing.exe")));
        Assert.Equal(Path.Combine(_tmp, "real.exe"), PluginManager.Which(Touch("real.exe")));
        Assert.NotNull(PluginManager.Which("dotnet"));
    }

    private string Touch(string name)
    {
        var p = Path.Combine(_tmp, name);
        File.WriteAllText(p, "");
        return p;
    }

    [Fact]
    public void Colored_text_becomes_runs_with_palette_bright_256_and_rgb_colors()
    {
        var runs = TextDecode.Runs("\u001b[31mred\u001b[0m plain \u001b[91mbright\u001b[39m \u001b[1mbold\u001b[22m \u001b[38;5;196mx\u001b[38;5;240my\u001b[38;5;5mz\u001b[38;5;300mw\u001b[38;2;1;2;3mrgb\u001b[m end\u001b[2K\r\n");
        Assert.Equal(1, runs[0].Color);
        Assert.Equal(("red", false), (runs[0].Text, runs[0].Bold));
        Assert.Equal(9, runs.First(r => r.Text == "bright").Color);
        Assert.True(runs.First(r => r.Text == "bold").Bold);
        Assert.Equal(0x1000000 | (255 << 16), runs.First(r => r.Text == "x").Color);   // 196 in the 6x6x6 cube
        Assert.Equal(0x1000000 | (88 << 16) | (88 << 8) | 88, runs.First(r => r.Text == "y").Color);   // 240 on the gray ramp
        Assert.Equal(5, runs.First(r => r.Text == "z").Color);
        Assert.Equal(7, runs.First(r => r.Text == "w").Color);   // out of range: the default
        Assert.Equal(0x1000000 | (1 << 16) | (2 << 8) | 3, runs.First(r => r.Text == "rgb").Color);
        Assert.Null(runs.First(r => r.Text.StartsWith(" end", StringComparison.Ordinal)).Color);
        Assert.Equal("é\n\t\"/", TextDecode.Unescape("\\u00e9\\n\\t\\\"\\/"));
        Assert.Equal("line one\nline two", TextDecode.Clean("line one\\nline two"));
        Assert.Equal("plain", TextDecode.Clean("\u001b[31mplain\u001b[0m"));
    }
}
