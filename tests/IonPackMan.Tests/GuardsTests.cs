using IonPackMan.Deployment;
using Xunit;

namespace IonPackMan.Tests;

public class GuardsTests
{
    private static string Joined(List<string> problems) => string.Join(" | ", problems);

    private static DeploymentPlan ValidPlan(TestDeployEnvironment env) => env.Planner.Build(TestDeployEnvironment.All()).Plan!;

    [Fact]
    public void ValidPlan_PassesAllChecks()
    {
        using var env = new TestDeployEnvironment();

        Assert.Empty(env.Guards.Check(ValidPlan(env)));
    }

    [Fact]
    public void EnvironmentThatIsNotDevelopment_Fails()
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env);
        var guards = new Guards(env.MachineWith("Test", env.Cache), env.Allow, env.Builds, () => [], _ => long.MaxValue / 2);

        Assert.Contains("only runs on Development machines", Joined(guards.Check(plan)));
    }

    [Fact]
    public void TargetOutsideBaseDirectory_Fails_AndSkipsFileChecks()
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env) with { TargetDirectory = Path.Combine(env.Root, "elsewhere") };

        var problems = env.Guards.Check(plan);

        Assert.Contains("not inside an allowed base directory", Joined(problems));
    }

    [Theory]
    [InlineData(@"..\..\outside.txt")]
    [InlineData(@"..\Mw\evil.dll")]   // keluar dari target walau masih di base directory
    public void FileThatEscapesTheTarget_Fails(string relative)
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env);
        var bad = plan.Files[0] with { RelativePath = relative, TargetPath = Path.Combine(plan.TargetDirectory, relative) };

        var problems = env.Guards.Check(plan with { Files = [bad] });

        Assert.Contains("outside the target directory", Joined(problems));
    }

    [Fact]
    public void SourceOutsideTheImportedBuild_Fails()
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env);
        var bad = plan.Files[0] with { SourcePath = Path.Combine(env.Root, "somewhere", "WIND.exe") };

        Assert.Contains("outside the imported build", Joined(env.Guards.Check(plan with { Files = [bad] })));
    }

    [Fact]
    public void RegistryIniAndServiceOutsideAllowList_Fail()
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env) with
        {
            RegistryChanges = [new("X", @"HKLM\SOFTWARE\Microsoft\Windows", "V", RegistryValueType.DWord, "1", null)],
            IniChanges = [new("Y", Path.Combine(env.Root, "other.ini"), "S", "K", "1", null)],
            ServiceName = "Spooler",
        };

        var text = Joined(env.Guards.Check(plan));

        Assert.Contains("Registry key", text);
        Assert.Contains("INI file", text);
        Assert.Contains("Service 'Spooler' is not in the allow-list", text);
    }

    [Fact]
    public void BuildChangedAfterPreview_FailsAtExecuteTime()
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env);
        Assert.Empty(env.Guards.Check(plan));

        File.WriteAllText(Path.Combine(plan.BuildFilesDirectory, "config.txt"), "changed-after-preview");

        Assert.Contains("hash differs", Joined(env.Guards.Check(plan)));
    }

    [Fact]
    public void BuildCheck_IsSkippedWhenNotDeployingFiles()
    {
        using var env = new TestDeployEnvironment();
        var plan = env.Planner.Build(new("TESTP", "wind", "2.1.0", false, true, false)).Plan!;
        File.WriteAllText(Path.Combine(Paths.BuildFilesDir(env.Cache, "wind", "2.1.0"), "config.txt"), "x");

        Assert.Empty(env.Guards.Check(plan));
    }

    [Fact]
    public void NotEnoughDiskSpace_FailsForBackupAndCopy()
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env);
        env.FreeSpace = 10;   // hampir penuh

        var text = Joined(env.Guards.Check(plan));

        Assert.Contains("free disk space for the backup", text);
        Assert.Contains("free disk space for the copy", text);
    }

    [Fact]
    public void DiskCheck_FailureToReadIsReported_NotSwallowed()
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env);
        var guards = new Guards(env.Machine, env.Allow, env.Builds, () => [], _ => throw new IOException("drive gone"));

        Assert.Contains("drive gone", Joined(guards.Check(plan)));
    }

    [Fact]
    public void UnfinishedBackup_Fails()
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env);
        env.UnfinishedBackups.Add("e1 (RestoreIncomplete)");

        Assert.Contains("e1 (RestoreIncomplete)", Joined(env.Guards.Check(plan)));
    }

    [Fact]
    public void JunctionInsideBaseDirectory_IsRefused()
    {
        using var env = new TestDeployEnvironment();
        string outside = Path.Combine(env.Root, "outside");
        string link = Path.Combine(env.Apps, "Linked");
        Directory.CreateDirectory(outside);
        env.MakeJunction(link, outside);

        var plan = ValidPlan(env) with { TargetDirectory = Path.Combine(link, "WIND"), Files = [] };

        Assert.Contains("is a link", Joined(env.Guards.Check(plan)));
    }

    [Fact]
    public void ReparsePointFileInTarget_IsRefused()
    {
        using var env = new TestDeployEnvironment();
        var plan = ValidPlan(env);
        string victim = Path.Combine(env.Root, "victim.txt");
        File.WriteAllText(victim, "victim");
        string linkPath = Path.Combine(env.Target, "config.txt");
        try { File.CreateSymbolicLink(linkPath, victim); }
        catch (IOException) { return; }   // membuat symlink butuh hak khusus (admin/Developer Mode); tanpa itu tes ini tidak bisa dijalankan

        Assert.Contains("is a link", Joined(env.Guards.Check(plan)));
    }
}
