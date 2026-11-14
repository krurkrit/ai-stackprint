// Copyright (c) AI Stackprint contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AiStackprint.Services;

using Xunit;

namespace AiStackprint.Core.Tests;

public sealed class CrashReportTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("crashreport").FullName;

    public void Dispose() => Directory.Delete(_tmp, true);

    [Fact]
    public void Last_entry_is_the_newest_crash()
    {
        Assert.Null(CrashReport.LastEntry(_tmp));
        CrashLog.Write("The app closed after this error", new InvalidOperationException("first"), _tmp);
        CrashLog.Write("The app closed after this error", new ArgumentException("second"), _tmp);
        var e = CrashReport.LastEntry(_tmp);
        Assert.NotNull(e);
        Assert.Contains("second", e);
        Assert.DoesNotContain("first", e);
    }

    [Fact]
    public void Home_folder_and_user_name_are_removed()
    {
        var text = @"at Foo() in C:\Users\jdoe\src\a.cs and /home/jdoe/x, also C:/Users/jdoe/y; user JDoe";
        var r = CrashReport.Redact(text, @"C:\Users\jdoe", "jdoe");
        Assert.DoesNotContain("jdoe", r, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"~\src\a.cs", r);
        Assert.Contains("~/y", r);
        Assert.Contains("/home/<user>/x", r);
    }

    [Fact]
    public void Link_opens_a_prefilled_issue_on_this_project_and_fits()
    {
        var entry = "2026-11-14 10:00:00 · The app closed after this error · v0.2.0 · Windows\nSystem.InvalidOperationException: boom\n"
            + string.Concat(Enumerable.Repeat("   at Some.Long.Namespace.Type.Method(String arg) in ~/src/File.cs:line 42\n", 400));
        var url = CrashReport.IssueUrl(entry);
        Assert.StartsWith($"https://github.com/{UpdateCheck.Repo}/issues/new?", url, StringComparison.Ordinal);
        Assert.True(url.Length <= CrashReport.MaxUrl, $"{url.Length} characters");
        var q = Uri.UnescapeDataString(url);
        Assert.Contains("title=Crash: System.InvalidOperationException: boom", q);
        Assert.Contains("v0.2.0", q);
        Assert.Contains("Cut to fit the link", q);
    }
}
