using IonPackMan.Deployment;
using IonPackMan.Machine;
using IonPackMan.Profiles;
using Xunit;

namespace IonPackMan.Tests;

public class ProfileSwitcherTests
{
    internal static ProfileSwitcher SwitcherFor(TestDeployEnvironment env)
    {
        var backups = new BackupStore(env.Cache, env.Registry, env.Services, env.Allow);
        var deployer = new Deployer(env.Machine, env.Guards, backups, env.Services, env.Processes, env.Registry,
            new IonPackMan.History.HistoryStore(env.Cache), TimeSpan.FromSeconds(1));
        var detector = new ProfileDetector(env.Components, env.Profiles, _ => true, _ => true);
        return new ProfileSwitcher(env.Machine, env.Allow, env.Profiles, env.Validator, env.Components, detector, env.Services,
            env.Planner, deployer, TimeSpan.FromSeconds(1));
    }

    // TestDeployEnvironment: TESTP = ion-middleware (tanpa service, folder Mw belum ada, belum ada build) + wind (ada, service WIND Running).
    internal static void ImportMiddlewareBuild(TestDeployEnvironment env)
    {
        var (build, errors) = env.Builds.Import("ion-middleware", "1.8.2", env.SourceBuild);
        Assert.True(build is not null, string.Join("; ", errors));
    }

    private static string MachineIni(TestDeployEnvironment env) =>
        File.Exists(env.Machine.SourcePath) ? File.ReadAllText(env.Machine.SourcePath) : "";

    [Fact]
    public void Build_DeploysEveryComponentInDependencyOrder_ConfigOnlyOnTheFirst()
    {
        using var env = new TestDeployEnvironment();
        ImportMiddlewareBuild(env);

        var (plan, errors) = SwitcherFor(env).Build("TESTP");

        Assert.Empty(errors);
        Assert.Equal(["ion-middleware", "wind"], plan!.Steps.Select(s => s.ComponentId));
        Assert.Equal(["DeployFiles", "ApplyConfig"], plan.Steps[0].OperationNames);
        Assert.Equal(["DeployFiles", "RestartService"], plan.Steps[1].OperationNames);   // wind: service ada, jadi dinyalakan
    }

    [Fact]
    public void Run_DeploysAll_AppliesConfig_AndMarksTheProfileActive()
    {
        using var env = new TestDeployEnvironment();
        ImportMiddlewareBuild(env);
        var switcher = SwitcherFor(env);
        var plan = switcher.Build("TESTP").Plan!;

        var result = switcher.Run(plan);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Empty(result.Warnings ?? []);
        Assert.True(File.Exists(Path.Combine(env.Apps, "Mw", "WIND.exe")));
        Assert.Equal("new-exe", File.ReadAllText(Path.Combine(env.Target, "WIND.exe")));   // yang sudah ada ditimpa build profile
        Assert.Equal("keep", File.ReadAllText(Path.Combine(env.Target, "keep.txt")));
        Assert.Equal("1", env.Registry.Read(TestDeployEnvironment.RegKey, "Support")!.Value);
        Assert.Contains("Start:WIND", env.Services.Calls);
        Assert.Equal("TESTP", env.Machine.Profile);
        Assert.Contains("Profile=TESTP", MachineIni(env));
    }

    [Fact]
    public void ComponentWithoutImportedBuild_RejectsThePlanBeforeAnythingChanges()
    {
        using var env = new TestDeployEnvironment();   // ion-middleware tidak punya build

        var (plan, errors) = SwitcherFor(env).Build("TESTP");

        Assert.Null(plan);
        Assert.Contains(errors, e => e.StartsWith("ion-middleware:") && e.Contains("No imported build"));
        Assert.Equal("old-exe", File.ReadAllText(Path.Combine(env.Target, "WIND.exe")));
        Assert.Equal("", MachineIni(env));
    }

    private static void AddMiddlewareOnlyProfile(TestDeployEnvironment env) =>
        Assert.Empty(env.Profiles.Save(new Profile { Id = "NEWP", Version = "1.0.0", Components = [new("ion-middleware", "1.8.2", [])] }));

    [Fact]
    public void SwitchingAway_StopsServicesOfTheOldProfileThatTheNewOneDoesNotUse()
    {
        using var env = new TestDeployEnvironment();
        ImportMiddlewareBuild(env);
        AddMiddlewareOnlyProfile(env);
        env.Machine.Profile = "TESTP";   // aktif: wind (service WIND, Running) + ion-middleware
        var switcher = SwitcherFor(env);

        var plan = switcher.Build("NEWP").Plan!;
        Assert.Equal("TESTP", plan.PreviousProfileId);
        Assert.Equal([new ServiceToStop("wind", "WIND")], plan.ServicesToStop);

        var result = switcher.Run(plan);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Warnings ?? []);
        Assert.Equal(ServiceState.Stopped, env.Services.GetState("WIND"));
        Assert.Equal("NEWP", env.Machine.Profile);
        Assert.Contains("Profile=NEWP", MachineIni(env));
    }

    [Fact]
    public void ServiceThatCannotBeStopped_LeavesTheOldProfileActive_AndSaysSo()
    {
        using var env = new TestDeployEnvironment();
        ImportMiddlewareBuild(env);
        AddMiddlewareOnlyProfile(env);
        env.Machine.Profile = "TESTP";
        env.Services.NeverStops("WIND");
        var switcher = SwitcherFor(env);
        var plan = switcher.Build("NEWP").Plan!;

        var result = switcher.Run(plan);

        Assert.Contains(result.Warnings!, w => w.Contains("WIND") && w.Contains("could not be stopped"));
        Assert.Contains(result.Warnings!, w => w.Contains("NOT activated"));
        Assert.Equal("TESTP", env.Machine.Profile);
        Assert.Equal("", MachineIni(env));
    }

    [Fact]
    public void FailedComponent_StopsTheSwitch_NamesWhatDidNotRun_AndDoesNotActivate()
    {
        using var env = new TestDeployEnvironment();
        ImportMiddlewareBuild(env);
        var switcher = SwitcherFor(env);
        var plan = switcher.Build("TESTP").Plan!;
        env.Services.FailOn("Start", "WIND", new InvalidOperationException("boom"));

        var result = switcher.Run(plan);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.StartsWith("wind:") && e.Contains("boom"));
        Assert.Equal("old-exe", File.ReadAllText(Path.Combine(env.Target, "WIND.exe")));   // wind dikembalikan dari backup
        Assert.Equal("", env.Machine.Profile);
        Assert.Equal("", MachineIni(env));
    }
}
