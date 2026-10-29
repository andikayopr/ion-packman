using IonPackMan.Builds;
using IonPackMan.Deployment;
using IonPackMan.Machine;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>ADR-006: Client sebagai atribut build (clients.ini, import, Guards, PlanBuilder).</summary>
public class ClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-client-" + Guid.NewGuid().ToString("N"));

    public ClientTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly string[] Known = ["wind", "qraptor", "ion-middleware"];

    private const string DefaultIni = "[Meta]\r\nSchemaVersion=1.0\r\n[Clients]\r\nBCA=Bank BCA\r\nBRI=Bank BRI\r\n[Overrides]\r\nBCA=wind,qraptor\r\nBRI=ion-middleware\r\n";

    private ClientConfig LoadConfig(string text = DefaultIni)
    {
        string path = Path.Combine(_dir, "clients.ini");
        File.WriteAllText(path, text);
        var (config, errors) = ClientConfig.Load(path, Known);
        Assert.True(config is not null, string.Join("; ", errors));
        return config;
    }

    // ---------- ClientConfig ----------

    [Fact]
    public void Load_MissingFile_MeansNoClients()
    {
        var (config, errors) = ClientConfig.Load(Path.Combine(_dir, "none.ini"), Known);

        Assert.Empty(errors);
        Assert.Empty(config!.Clients);
        Assert.Equal("", config.BuildClientFor("BCA", "wind"));
    }

    [Fact]
    public void BuildClientFor_UsesClientOnlyForOverriddenComponents()
    {
        var config = LoadConfig();

        Assert.Equal("BCA", config.BuildClientFor("bca", "wind"));
        Assert.Equal("", config.BuildClientFor("BCA", "ion-middleware"));
        Assert.Equal("BRI", config.BuildClientFor("BRI", "ion-middleware"));
        Assert.Equal("", config.BuildClientFor("", "wind"));
    }

    [Fact]
    public void Suggest_MatchesBranchWord_OrNull()
    {
        var config = LoadConfig();

        Assert.Equal("BCA", config.Suggest("feature/bca-qristap", "wind"));
        Assert.Null(config.Suggest("main", "wind"));
        Assert.Null(config.Suggest("feature/bri-offline", "wind"));   // BRI tidak meng-override wind
    }

    [Theory]
    [InlineData("[Clients]\r\nBCA=x\r\n[Overrides]\r\nBRI=wind\r\n", "not listed in [Clients]")]
    [InlineData("[Clients]\r\nBCA=x\r\n[Overrides]\r\nBCA=unknown\r\n", "not a component in catalog.ini")]
    [InlineData("[Clients]\r\nB@A=x\r\n", "not a valid Client Id")]
    public void Load_InvalidFile_ReportsProblem(string body, string expected)
    {
        string path = Path.Combine(_dir, "bad.ini");
        File.WriteAllText(path, "[Meta]\r\nSchemaVersion=1.0\r\n" + body);

        var (config, errors) = ClientConfig.Load(path, Known);

        Assert.Null(config);
        Assert.Contains(expected, string.Join(" | ", errors));
    }

    // ---------- BuildImporter ----------

    private string MakeSource(string content)
    {
        string src = Path.Combine(_dir, "src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "wind.exe"), content);
        return src;
    }

    [Fact]
    public void Import_WithClient_UsesSeparateFolderAndDoesNotOverwriteDefault()
    {
        string cache = Path.Combine(_dir, "cache");
        var importer = new BuildImporter(cache, _ => long.MaxValue / 2, LoadConfig());

        Assert.Empty(importer.Import("wind", "2.1.0", MakeSource("default")).Errors);
        var (build, errors) = importer.Import("wind", "2.1.0", MakeSource("bca"), client: "bca");

        Assert.Empty(errors);
        Assert.Equal("BCA", build!.Client);
        Assert.Equal("default", File.ReadAllText(Path.Combine(Paths.BuildFilesDir(cache, "wind", "2.1.0"), "wind.exe")));
        Assert.Equal("bca", File.ReadAllText(Path.Combine(Paths.BuildFilesDir(cache, "wind", "2.1.0", "BCA"), "wind.exe")));
        Assert.Contains("Client=BCA", File.ReadAllText(Paths.BuildIni(cache, "wind", "2.1.0", "BCA")));
        Assert.Equal("BCA", importer.Load("wind", "2.1.0", "BCA").Build!.Client);
        Assert.True(importer.Exists("wind", "2.1.0", "BCA"));
        Assert.Empty(importer.Verify("wind", "2.1.0", client: "BCA"));

        Assert.Equal(["", "BCA"], importer.ListBuilds().Select(b => b.Client));
    }

    [Fact]
    public void Import_ClientThatDoesNotOverrideComponent_IsRejected()
    {
        string cache = Path.Combine(_dir, "cache");
        var importer = new BuildImporter(cache, _ => long.MaxValue / 2, LoadConfig());

        var (build, errors) = importer.Import("wind", "2.1.0", MakeSource("x"), client: "BRI");

        Assert.Null(build);
        Assert.Contains("is not a Client that overrides wind", string.Join(" ", errors));
        Assert.False(Directory.Exists(Paths.BuildsDir(cache)));
    }

    // ---------- Guards dan PlanBuilder ----------

    private sealed record ClientRig(TestDeployEnvironment Env, BuildImporter Builds, Guards Guards, PlanBuilder Planner) : IDisposable
    {
        public void Dispose() => Env.Dispose();
    }

    // Mesin Client=BCA; build default wind@2.1.0 sudah ada dari TestDeployEnvironment.
    private ClientRig BcaRig(bool importBcaBuild)
    {
        var env = new TestDeployEnvironment();
        var clients = LoadConfig();
        var builds = new BuildImporter(env.Cache, clients: clients);
        if (importBcaBuild)
            Assert.Empty(builds.Import("wind", "2.1.0", env.SourceBuild, client: "BCA").Errors);

        var machine = new MachineConfig
        {
            MachineName = "TEST-PC", Environment = "Development", Client = "BCA", SourcePath = Path.Combine(env.Root, "machine.ini"),
            InstallLocations = new Dictionary<string, string> { ["TEST"] = env.Apps }, LocalCacheDirectory = env.Cache,
        };
        var guards = new Guards(machine, env.Allow, builds, () => [], _ => long.MaxValue / 2, clients);
        var planner = new PlanBuilder(machine, env.Allow, env.Components, env.Profiles, env.Validator, builds, env.Registry,
            env.Services, env.Processes, guards, clients);
        return new ClientRig(env, builds, guards, planner);
    }

    [Fact]
    public void Plan_OnClientMachine_UsesClientBuildForOverriddenComponent()
    {
        using var rig = BcaRig(importBcaBuild: true);

        var (plan, errors) = rig.Planner.Build(TestDeployEnvironment.All());

        Assert.Empty(errors);
        Assert.Equal("BCA", plan!.BuildClient);
        Assert.Contains("@2.1.0@BCA", plan.BuildFilesDirectory);
        Assert.Empty(rig.Guards.Check(plan));
    }

    [Fact]
    public void Plan_OnClientMachine_WithoutClientBuild_DoesNotFallBackToDefault()
    {
        using var rig = BcaRig(importBcaBuild: false);

        var (plan, errors) = rig.Planner.Build(TestDeployEnvironment.All());

        Assert.Null(plan);
        Assert.Contains("default build is not accepted", string.Join(" ", errors));
    }

    [Fact]
    public void Plan_ForComponentWithoutOverride_UsesDefault()
    {
        using var rig = BcaRig(importBcaBuild: true);
        Assert.Empty(rig.Builds.Import("ion-middleware", "1.8.2", rig.Env.SourceBuild).Errors);   // default

        var (plan, errors) = rig.Planner.Build(TestDeployEnvironment.All("ion-middleware", "1.8.2"));

        Assert.Empty(errors);
        Assert.Equal("", plan!.BuildClient);
    }

    [Fact]
    public void Guards_RejectPlanWithWrongBuildClient()
    {
        using var rig = BcaRig(importBcaBuild: true);
        var plan = rig.Planner.Build(TestDeployEnvironment.All()).Plan!;

        var problems = rig.Guards.Check(plan with { BuildClient = "" });

        Assert.Contains("only the BCA build can be deployed", string.Join(" | ", problems));
    }
}
