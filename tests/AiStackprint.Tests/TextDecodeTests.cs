using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Tests;

public class TextDecodeTests
{
    const string Esc = "\u001b";

    [Fact]
    public void Strip_removes_colors_and_other_escape_sequences()
    {
        Assert.Equal("red ok", TextDecode.Strip($"{Esc}[31mred{Esc}[0m ok"));
        Assert.Equal("title text", TextDecode.Strip($"{Esc}]0;window{Esc}\\title text"));
        Assert.Equal("plain", TextDecode.Strip("plain"));
    }

    [Fact]
    public void Runs_follow_sgr_codes()
    {
        var runs = TextDecode.Runs($"a{Esc}[1;31mb{Esc}[0mc{Esc}[38;5;2md{Esc}[38;2;1;2;3me");
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, runs.Select(r => r.Text));
        Assert.Null(runs[0].Color);
        Assert.Equal(1, runs[1].Color); Assert.True(runs[1].Bold);
        Assert.Null(runs[2].Color); Assert.False(runs[2].Bold);
        Assert.Equal(2, runs[3].Color);
        Assert.Equal(0x1000000 | (1 << 16) | (2 << 8) | 3, runs[4].Color);
    }

    [Fact]
    public void Unescape_decodes_json_escapes_in_one_pass()
    {
        Assert.Equal($"{Esc}[32mhi\nthere", TextDecode.Unescape("\\u001b[32mhi\\nthere"));
        Assert.Equal("a\\nb", TextDecode.Unescape("a\\\\nb"));   // an escaped backslash followed by n stays two characters
        Assert.Equal("\U0001F600", TextDecode.Unescape("\\ud83d\\ude00"));
    }

    [Fact]
    public void LooksEscaped_ignores_code_that_only_contains_a_newline_escape()
    {
        Assert.True(TextDecode.LooksEscaped("one line with \\u001b[31m and \\n inside"));
        Assert.False(TextDecode.LooksEscaped("printf \"a\\n\"\nline two\nline three\nline four"));
        Assert.False(TextDecode.LooksEscaped("nothing special"));
    }

    [Fact]
    public void Prepare_pretty_prints_json_also_inside_a_json_string()
    {
        var p = TextDecode.Prepare("{\"a\":1,\"b\":[1,2]}", null);
        Assert.Equal(".json", p.Lang); Assert.True(p.Changed); Assert.Contains("\n  \"a\": 1", p.Text);
        var q = TextDecode.Prepare("\"{\\\"a\\\":1}\"", null);
        Assert.Equal(".json", q.Lang); Assert.Contains("\"a\": 1", q.Text);
        var e = TextDecode.Prepare("{\\\"a\\\":1}", ".txt");   // JSON whose quotes are escaped
        Assert.Equal(".json", e.Lang); Assert.Contains("\"a\": 1", e.Text);
    }

    [Fact]
    public void Prepare_parses_colors_and_keeps_plain_text_unchanged()
    {
        var c = TextDecode.Prepare("\\u001b[31mfail\\u001b[0m\\nnext", null);
        Assert.NotNull(c.Ansi); Assert.Equal("fail\nnext", c.Text);
        var plain = TextDecode.Prepare("just text", ".txt");
        Assert.False(plain.Changed); Assert.Equal(".txt", plain.Lang); Assert.Null(plain.Ansi);
        var cut = TextDecode.Prepare("{\"a\": [1, 2", null);   // cut off: not JSON, left as it is
        Assert.False(cut.Changed); Assert.Null(cut.Lang);
    }

    [Fact]
    public void Prepare_shortens_inline_image_data()
    {
        var p = TextDecode.Prepare("img data:image/png;base64," + new string('A', 4000) + " end", null);
        Assert.True(p.Changed);
        Assert.Contains("[image data, 2 KB]", p.Text);
        Assert.DoesNotContain("AAAA", p.Text);
    }
}
