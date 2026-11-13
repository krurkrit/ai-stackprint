using AiStackprint.Models;
using AiStackprint.Services;
using Xunit;

namespace AiStackprint.Core.Tests;

public class SkillManagerTests : IDisposable
{
    readonly string _tmp = Directory.CreateTempSubdirectory("aiprof-").FullName;
    public void Dispose() => Directory.Delete(_tmp, true);

    string MakeSkill(string parent, string name)
    {
        var d = Path.Combine(parent, name);
        Directory.CreateDirectory(Path.Combine(d, "refs"));
        File.WriteAllText(Path.Combine(d, "SKILL.md"), "---\nname: " + name + "\n---\n");
        File.WriteAllText(Path.Combine(d, "refs", "a.md"), "a");
        return d;
    }

    static Item Skill(string path, string level = "User", string? plugin = null) =>
        new("Skill", "s", "", "App", level, path) { Plugin = plugin };

    [Fact]
    public void Install_copies_folder_with_subfolders()
    {
        var src = MakeSkill(Path.Combine(_tmp, "src"), "pdf");
        var dest = SkillManager.Install(src, Path.Combine(_tmp, "root"));
        Assert.True(File.Exists(Path.Combine(dest, "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(dest, "refs", "a.md")));
        Assert.True(Directory.Exists(src));
    }

    [Fact]
    public void Install_refuses_folder_without_skill_md_and_existing_name()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_tmp, "empty")).FullName;
        Assert.Throws<InvalidOperationException>(() => SkillManager.Install(empty, Path.Combine(_tmp, "root")));
        var src = MakeSkill(Path.Combine(_tmp, "src"), "pdf");
        SkillManager.Install(src, Path.Combine(_tmp, "root"));
        Assert.Throws<InvalidOperationException>(() => SkillManager.Install(src, Path.Combine(_tmp, "root")));
    }

    [Fact]
    public void Remove_moves_folder_to_backup()
    {
        var dir = MakeSkill(Path.Combine(_tmp, "root"), "pdf");
        var backup = SkillManager.Remove(Skill(Path.Combine(dir, "SKILL.md")), Path.Combine(_tmp, "bak"));
        Assert.False(Directory.Exists(dir));
        Assert.True(File.Exists(Path.Combine(backup, "SKILL.md")));
        Assert.StartsWith(Path.Combine(_tmp, "bak"), backup);
    }

    [Fact]
    public void Remove_moves_flat_md_file()
    {
        var f = Path.Combine(_tmp, "flat.md");
        File.WriteAllText(f, "x");
        var backup = SkillManager.Remove(Skill(f), Path.Combine(_tmp, "bak"));
        Assert.False(File.Exists(f));
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public void Remove_refuses_system_and_plugin_skills()
    {
        var dir = MakeSkill(Path.Combine(_tmp, "root"), "pdf");
        var file = Path.Combine(dir, "SKILL.md");
        Assert.Throws<InvalidOperationException>(() => SkillManager.Remove(Skill(file, "System"), _tmp));
        Assert.Throws<InvalidOperationException>(() => SkillManager.Remove(Skill(file, plugin: "p@m"), _tmp));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void TargetRoot_picks_first_matching_folder_root()
    {
        var d = new AppDefinition
        {
            Roots = { new ScanRoot { Path = "/sys/skills", Level = "System" }, new ScanRoot { Path = "/u/flat", Layout = "flat-md" }, new ScanRoot { Path = "/u/skills" } },
            ProjectRoots = { new ScanRoot { Path = ".app/skills", Level = "Project" } }
        };
        Assert.Equal("/u/skills", SkillManager.TargetRoot(d, "User", null)!.Replace('\\', '/'));
        Assert.Equal(Path.Combine(_tmp, ".app", "skills"), SkillManager.TargetRoot(d, "Project", _tmp));
        Assert.Null(SkillManager.TargetRoot(d, "Project", null));
        Assert.Null(SkillManager.TargetRoot(d, "System", null));
    }
}
