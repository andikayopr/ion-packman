using IonPackMan.Deployment;
using Xunit;

namespace IonPackMan.Tests;

public class PlanBuilderTests
{
    private static string Joined(List<string> errors) => string.Join(" | ", errors);

    [Fact]
    public void FullPlan_ListsFilesConfigAndServiceSteps()
    {
        using var env = new TestDeployEnvironment();

        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All());

        Assert.Empty(errors);
        Assert.NotNull(plan);
        Assert.Equal("TESTP", plan.ProfileId);
        Assert.Equal(env.Target, plan.TargetDirectory);

        // keep.txt ada di target tetapi tidak di build: tidak masuk plan, jadi tidak pernah disentuh/dihapus.
        Assert.Equal(new[] { "WIND.exe", "config.txt", "lib/x.dll" }, plan.Files.Select(f => f.RelativePath));
        Assert.Equal(FileChange.Replace, plan.Files.Single(f => f.RelativePath == "WIND.exe").Change);
        Assert.Equal(FileChange.New, plan.Files.Single(f => f.RelativePath == "config.txt").Change);
        Assert.Equal(Path.Combine(env.Target, "lib", "x.dll"), plan.Files.Single(f => f.RelativePath == "lib/x.dll").TargetPath);

        var reg = Assert.Single(plan.RegistryChanges);
        Assert.Equal(("Support", "0", "1", RegistryValueType.DWord), (reg.ValueName, reg.CurrentValue, reg.NewValue, reg.Type));
        var ini = Assert.Single(plan.IniChanges);
        Assert.Equal(("WebUI", "Support", "0", "1"), (ini.Section, ini.Key, ini.CurrentValue, ini.NewValue));

        Assert.True(plan.StopServiceBefore);    // Deploy files + service Running
        Assert.True(plan.StartServiceAfter);
        Assert.Equal(new[] { "DeployFiles", "ApplyConfig" }, plan.OperationNames);
    }

    [Fact]
    public void FileWithSameContentAsTarget_IsMarkedSame()
    {
        using var env = new TestDeployEnvironment();
        File.WriteAllText(Path.Combine(env.Target, "config.txt"), "cfg");   // identik dengan build

        var plan = env.Planner.Build(TestDeployEnvironment.All()).Plan!;

        Assert.Equal(FileChange.Same, plan.Files.Single(f => f.RelativePath == "config.txt").Change);
    }

    [Fact]
    public void Build_OnlyReads_NothingOnTheMachineChanges()
    {
        using var env = new TestDeployEnvironment();
        var folder = TestDeployEnvironment.SnapshotFolder(env.Apps);
        var registry = env.Registry.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value);
        env.Services.Calls.Clear();

        env.Planner.Build(TestDeployEnvironment.All());

        Assert.Equal(folder, TestDeployEnvironment.SnapshotFolder(env.Apps));
        Assert.Equal(registry, env.Registry.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value));
        Assert.DoesNotContain(env.Services.Calls, c => c.StartsWith("Stop") || c.StartsWith("Start"));
        Assert.DoesNotContain(env.Registry.Operations, o => o.StartsWith("Write") || o.StartsWith("Delete"));
    }

    [Fact]
    public void StoppedService_DeployOnly_AddsNoServiceSteps()
    {
        using var env = new TestDeployEnvironment(wind: ServiceState.Stopped);

        var plan = env.Planner.Build(new("TESTP", "wind", "2.1.0", DeployFiles: true, ApplyConfig: false, RestartService: false)).Plan!;

        Assert.False(plan.StopServiceBefore);
        Assert.False(plan.StartServiceAfter);
    }

    [Fact]
    public void RestartOnly_OnStoppedService_JustStartsIt()
    {
        using var env = new TestDeployEnvironment(wind: ServiceState.Stopped);

        var plan = env.Planner.Build(new("TESTP", "wind", "2.1.0", DeployFiles: false, ApplyConfig: false, RestartService: true)).Plan!;

        Assert.False(plan.StopServiceBefore);
        Assert.True(plan.StartServiceAfter);
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void ApplyConfigOnly_DoesNotRestartTheService()
    {
        using var env = new TestDeployEnvironment();

        var plan = env.Planner.Build(new("TESTP", "wind", "2.1.0", DeployFiles: false, ApplyConfig: true, RestartService: false)).Plan!;

        Assert.False(plan.StopServiceBefore);
        Assert.False(plan.StartServiceAfter);
        Assert.Empty(plan.Files);
        Assert.Single(plan.RegistryChanges);
    }

    [Fact]
    public void NoOperationSelected_IsBlocked()
    {
        using var env = new TestDeployEnvironment();

        var (plan, errors) = env.Planner.Build(new("TESTP", "wind", "2.1.0", false, false, false));

        Assert.Null(plan);
        Assert.Contains("Select at least one operation.", errors);
    }

    [Fact]
    public void ComponentNotInProfile_IsBlocked()
    {
        using var env = new TestDeployEnvironment();

        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All("qraptor", "1.3.1"));

        Assert.Null(plan);
        Assert.Contains("not part of profile 'TESTP'", Joined(errors));
    }

    [Fact]
    public void UnknownProfile_IsBlocked()
    {
        using var env = new TestDeployEnvironment();

        var (plan, errors) = env.Planner.Build(new("NOPE", "wind", "2.1.0", true, false, false));

        Assert.Null(plan);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void DeployWithoutImportedBuild_SaysToImportItInComponents()
    {
        using var env = new TestDeployEnvironment(importBuild: false);

        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All());

        Assert.Null(plan);
        Assert.Contains("Import it in Components first", Joined(errors));
    }

    [Fact]
    public void ConfigOnly_DoesNotNeedAnImportedBuild()
    {
        using var env = new TestDeployEnvironment(importBuild: false);

        var (plan, errors) = env.Planner.Build(new("TESTP", "wind", "2.1.0", false, true, false));

        Assert.Empty(errors);
        Assert.NotNull(plan);
    }

    [Fact]
    public void TamperedBuildInCache_IsBlocked()
    {
        using var env = new TestDeployEnvironment();
        File.WriteAllText(Path.Combine(Paths.BuildFilesDir(env.Cache, "wind", "2.1.0"), "WIND.exe"), "tampered");

        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All());

        Assert.Null(plan);
        Assert.Contains("is not valid", Joined(errors));
        Assert.Contains("hash differs", Joined(errors));
    }

    [Fact]
    public void TargetOutsideBaseDirectory_IsBlocked()
    {
        using var env = new TestDeployEnvironment();
        // Disimpan tanpa allow-list (seolah file component diedit manual), lalu PlanBuilder harus menolaknya.
        Assert.Empty(env.Components.Save(new Components.ComponentRelease
        {
            Id = "wind", ReleaseVersion = "3.0.0", TargetDirectory = @"C:\Windows\System32", ServiceName = "WIND",
        }));

        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All("wind", "3.0.0"));

        Assert.Null(plan);
        Assert.Contains("not inside an allowed base directory", Joined(errors));
    }

    [Fact]
    public void RestartWithoutServiceName_AndNoProgramRunning_IsBlocked()
    {
        using var env = new TestDeployEnvironment();

        var (plan, errors) = env.Planner.Build(new("TESTP", "ion-middleware", "1.8.2", false, false, true));

        Assert.Null(plan);
        Assert.Contains("no program is running", Joined(errors));
    }

    [Fact]
    public void ComponentWithoutService_PlansToRestartTheRunningProgram()
    {
        using var env = new TestDeployEnvironment();
        string exe = Path.Combine(env.Apps, "Mw", "IonMiddleware.exe");
        env.Processes.Run(exe);

        var plan = env.Planner.Build(new("TESTP", "ion-middleware", "1.8.2", false, false, true)).Plan!;

        Assert.True(plan.RestartsPrograms);
        Assert.Equal([exe], plan.ProgramsAtPlan);
        Assert.False(plan.StopServiceBefore);
        Assert.False(plan.StartServiceAfter);
    }

    [Fact]
    public void ApplyConfigOnly_DoesNotRestartTheProgram()
    {
        using var env = new TestDeployEnvironment();
        env.Processes.Run(Path.Combine(env.Apps, "Mw", "IonMiddleware.exe"));

        var plan = env.Planner.Build(new("TESTP", "ion-middleware", "1.8.2", false, true, false)).Plan!;

        Assert.False(plan.RestartsPrograms);
    }

    [Fact]
    public void ConfigFromTheGlobalCatalog_IsPlannedForAProfileWithoutOwnEntries()
    {
        using var env = new TestDeployEnvironment();
        string catalogFile = Path.Combine(env.Root, "config-entries.ini");
        File.WriteAllText(catalogFile, $"""
            [Meta]
            SchemaVersion=1.0
            [Registry:WebUISupport]
            Key={TestDeployEnvironment.RegKey}
            ValueName=Support
            Type=DWORD
            Value=1
            Feature=WindWebUI
            [Ini:WebUISupport]
            File={env.AppIni}
            Section=WebUI
            Key=Support
            Value=1
            Feature=WindWebUI
            """);
        var (catalog, catalogErrors) = Profiles.ConfigCatalog.Load(catalogFile);
        Assert.Empty(catalogErrors);
        Assert.Empty(env.Profiles.Save(new Profiles.Profile
        {
            Id = "BARE", Version = "1.0.0", WindWebUIEnabled = true,
            Components = [new("ion-middleware", "1.8.2", []), new("wind", "2.1.0", ["ion-middleware"])],
        }));
        var validator = new Profiles.ProfileValidator(env.Components, env.Allow, catalog);
        var planner = new PlanBuilder(env.Machine, env.Allow, env.Components, env.Profiles, validator, env.Builds, env.Registry,
            env.Services, env.Processes, env.Guards);

        var (plan, errors) = planner.Build(new("BARE", "wind", "2.1.0", false, true, false));

        Assert.Empty(errors);
        Assert.Equal("Support", Assert.Single(plan!.RegistryChanges).ValueName);
        Assert.Equal("Support", Assert.Single(plan.IniChanges).Key);
    }

    [Fact]
    public void SnapshotOption_RecordsFolderSizeInThePlan()
    {
        using var env = new TestDeployEnvironment();

        var plan = env.Planner.Build(TestDeployEnvironment.All() with { SnapshotFolder = true }).Plan!;

        Assert.True(plan.SnapshotFolder);
        Assert.Equal("old-exe".Length + "keep".Length, plan.SnapshotBytes);   // WIND.exe lama + keep.txt
    }

    [Fact]
    public void SnapshotOption_IsIgnoredWithoutDeployFiles()
    {
        using var env = new TestDeployEnvironment();

        var plan = env.Planner.Build(new("TESTP", "wind", "2.1.0", false, true, false, SnapshotFolder: true)).Plan!;

        Assert.False(plan.SnapshotFolder);
    }

    [Fact]
    public void SnapshotOption_NeedsFreeDiskSpaceForTheWholeFolder()
    {
        using var env = new TestDeployEnvironment();
        File.WriteAllBytes(Path.Combine(env.Target, "big.bin"), new byte[3 * 1024 * 1024]);
        env.FreeSpace = 3 * 1024 * 1024;   // cukup untuk build kecil, tidak untuk snapshot 3 MB + margin

        var withoutSnapshot = env.Planner.Build(TestDeployEnvironment.All());
        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All() with { SnapshotFolder = true });

        Assert.NotNull(withoutSnapshot.Plan);
        Assert.Null(plan);
        Assert.Contains("Not enough free disk space for the backup", Joined(errors));
    }

    [Fact]
    public void ServiceRunningOutsideTargetDirectory_BlocksDeployFiles()
    {
        using var env = new TestDeployEnvironment();
        env.Services.WithExecutable("WIND", @"C:\Elsewhere\WIND.exe");

        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All());

        Assert.Null(plan);
        Assert.Contains("outside the target directory", Joined(errors));
    }

    [Fact]
    public void ServiceRunningInsideTargetDirectory_IsAccepted()
    {
        using var env = new TestDeployEnvironment();
        string exe = Path.Combine(env.Target, "WIND.exe");
        env.Services.WithExecutable("WIND", exe);

        var plan = env.Planner.Build(TestDeployEnvironment.All()).Plan!;

        Assert.Equal(exe, plan.ServiceExecutable);
    }

    [Fact]
    public void ServiceRunningOutsideTargetDirectory_DoesNotBlockConfigOrRestartOnly()
    {
        using var env = new TestDeployEnvironment();
        env.Services.WithExecutable("WIND", @"C:\Elsewhere\WIND.exe");

        var (plan, errors) = env.Planner.Build(new("TESTP", "wind", "2.1.0", DeployFiles: false, ApplyConfig: true, RestartService: true));

        Assert.Empty(errors);
        Assert.NotNull(plan);
    }

    [Fact]
    public void RestartServiceNotFoundOnMachine_IsBlocked()
    {
        using var env = new TestDeployEnvironment();
        var empty = new FakeServiceControl();   // WIND tidak ada di mesin ini
        var planner = new PlanBuilder(env.Machine, env.Allow, env.Components, env.Profiles, env.Validator, env.Builds, env.Registry, empty, env.Processes, env.Guards);

        var (plan, errors) = planner.Build(new("TESTP", "wind", "2.1.0", false, false, true));

        Assert.Null(plan);
        Assert.Contains("was not found on this machine", Joined(errors));
    }

    [Fact]
    public void ApplyConfig_WithAllFeaturesOff_IsBlocked()
    {
        using var env = new TestDeployEnvironment();
        var profile = env.Profiles.Load("TESTP").Profile!.WithWindWebUI(false);
        Assert.Empty(env.Profiles.Save(profile));

        var (plan, errors) = env.Planner.Build(new("TESTP", "wind", "2.1.0", false, true, false));

        Assert.Null(plan);
        Assert.Contains("no enabled Registry/INI changes", Joined(errors));
    }

    [Fact]
    public void InvalidCacheDirectory_IsBlocked_WithAFixHint()
    {
        using var env = new TestDeployEnvironment();
        var planner = env.PlannerFor(env.MachineWith("Development", "relative-cache"));

        var (plan, errors) = planner.Build(TestDeployEnvironment.All());

        Assert.Null(plan);
        Assert.Contains("machine.ini", Joined(errors));
    }

    [Fact]
    public void NonDevelopmentMachine_IsBlockedByGuards()
    {
        using var env = new TestDeployEnvironment(environment: "Production");

        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All());

        Assert.Null(plan);
        Assert.Contains("only runs on Development machines", Joined(errors));
    }

    [Fact]
    public void UnfinishedBackup_BlocksNewPlans()
    {
        using var env = new TestDeployEnvironment();
        env.UnfinishedBackups.Add("20261028-101530-wind-3f9a (InProgress)");

        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All());

        Assert.Null(plan);
        Assert.Contains("unfinished backup must be restored first", Joined(errors));
    }
}
