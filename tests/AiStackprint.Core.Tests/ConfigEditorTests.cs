using System.Text;
using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Core.Tests;

public class ConfigEditorTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("aiprof-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    string File(string name, string text, bool bom = false)
    {
        var f = Path.Combine(_tmp, name);
        System.IO.File.WriteAllText(f, text, new UTF8Encoding(bom));
        return f;
    }

    [Fact]
    public void Save_backs_up_original_then_writes()
    {
        var f = File("settings.json", "{ \"a\": 1 }");
        var (_, stamp) = ConfigEditor.Read(f);
        var backup = ConfigEditor.Save(f, "{ \"a\": 2 }", stamp, Path.Combine(_tmp, "bak"));
        Assert.Equal("{ \"a\": 2 }", System.IO.File.ReadAllText(f));
        Assert.Equal("{ \"a\": 1 }", System.IO.File.ReadAllText(backup));
    }

    [Fact]
    public void Save_rejects_invalid_json_and_leaves_file_alone()
    {
        var f = File("settings.json", "{}");
        var (_, stamp) = ConfigEditor.Read(f);
        Assert.Throws<InvalidOperationException>(() => ConfigEditor.Save(f, "{ broken", stamp, _tmp));
        Assert.Equal("{}", System.IO.File.ReadAllText(f));
    }

    [Fact]
    public void Validate_allows_comments_and_trailing_commas_and_skips_non_json()
    {
        Assert.Null(ConfigEditor.Validate("a.json", "{ // c\n \"a\": 1, }"));
        Assert.Null(ConfigEditor.Validate("a.toml", "not = [valid"));
        Assert.NotNull(ConfigEditor.Validate("a.json", "{"));
    }

    [Fact]
    public void Save_refuses_when_file_changed_after_opening()
    {
        var f = File("CLAUDE.md", "one");
        var (_, stamp) = ConfigEditor.Read(f);
        System.IO.File.WriteAllText(f, "changed elsewhere");
        System.IO.File.SetLastWriteTimeUtc(f, stamp.Modified.AddSeconds(5));
        Assert.Throws<FileChangedException>(() => ConfigEditor.Save(f, "mine", stamp, _tmp));
        Assert.Equal("changed elsewhere", System.IO.File.ReadAllText(f));
    }

    [Fact]
    public void Save_keeps_byte_order_mark_choice()
    {
        var withBom = File("a.md", "x", bom: true);
        var (_, s1) = ConfigEditor.Read(withBom);
        ConfigEditor.Save(withBom, "y", s1, _tmp);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'y' }, System.IO.File.ReadAllBytes(withBom));
        var noBom = File("b.md", "x");
        var (_, s2) = ConfigEditor.Read(noBom);
        ConfigEditor.Save(noBom, "y", s2, _tmp);
        Assert.Equal(new[] { (byte)'y' }, System.IO.File.ReadAllBytes(noBom));
    }
}
