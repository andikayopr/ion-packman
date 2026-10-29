using System.Diagnostics;
using IonPackMan.Builds;
using IonPackMan.Components;
using IonPackMan.Deployment;
using IonPackMan.Machine;
using IonPackMan.Profiles;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>
/// "Mesin ATM palsu" untuk tes Deployment: semua di folder sementara, Registry dan service palsu.
/// Tidak ada tes yang menyentuh mesin sungguhan. Isi:
///   apps\WIND (target; WIND.exe lama + keep.txt), apps\Config\WebUIUXCNFG.ini, release\ (allow-list, component, profile),
///   cache\ (build wind@2.1.0 sudah diimpor dari src-build: WIND.exe baru, config.txt, lib\x.dll).
/// </summary>
internal sealed class TestDeployEnvironment : IDisposable
{
    public const string RegKey = @"HKLM\SOFTWARE\WOW6432Node\Test\WebUI";

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));
    public string Apps => Path.Combine(Root, "apps");
    public string Target => Path.Combine(Apps, "WIND");
    public string AppIni => Path.Combine(Apps, "Config", "WebUIUXCNFG.ini");
    public string Release => Path.Combine(Root, "release");
    public string Cache => Path.Combine(Root, "cache");
    public string SourceBuild => Path.Combine(Root, "src-build");

    public MachineConfig Machine { get; private set; } = null!;
    public AllowList Allow { get; private set; } = null!;
    public ComponentStore Components { get; private set; } = null!;
    public ProfileValidator Validator { get; private set; } = null!;
    public ProfileStore Profiles { get; private set; } = null!;
    public BuildImporter Builds { get; private set; } = null!;
    public FakeRegistry Registry { get; } = new();
    public FakeServiceControl Services { get; } = new();
    public FakeProcessControl Processes { get; } = new();

    /// <summary>Isi daftar ini untuk mensimulasikan backup yang belum selesai.</summary>
    public List<string> UnfinishedBackups { get; } = [];
    public long FreeSpace { get; set; } = long.MaxValue / 2;

    public Guards Guards { get; private set; } = null!;
    public PlanBuilder Planner { get; private set; } = null!;

    public TestDeployEnvironment(string environment = "Development", ServiceState wind = ServiceState.Running, bool importBuild = true)
    {
        Directory.CreateDirectory(Target);
        Directory.CreateDirectory(Path.Combine(Apps, "Config"));
        File.WriteAllText(Path.Combine(Target, "WIND.exe"), "old-exe");
        File.WriteAllText(Path.Combine(Target, "keep.txt"), "keep");
        File.WriteAllText(AppIni, "[WebUI]\r\nSupport=0\r\nOther=keep\r\n");

        WriteAllowList();
        Machine = MachineWith(environment, Cache);
        Components = new ComponentStore(Release);
        Validator = new ProfileValidator(Components, Allow);
        Profiles = new ProfileStore(Release, Validator);
        Builds = new BuildImporter(Cache);

        Assert.Empty(Components.Save(new ComponentRelease { Id = "wind", ReleaseVersion = "2.1.0", TargetDirectory = Target, ServiceName = "WIND" }, Allow));
        Assert.Empty(Components.Save(new ComponentRelease { Id = "ion-middleware", ReleaseVersion = "1.8.2", TargetDirectory = Path.Combine(Apps, "Mw") }, Allow));
        Assert.Empty(Profiles.Save(new Profile
        {
            Id = "TESTP", Version = "1.0.0", WindWebUIEnabled = true,
            Components = [new("ion-middleware", "1.8.2", []), new("wind", "2.1.0", ["ion-middleware"])],
            RegistryEntries = [new("WebUISupport", RegKey, "Support", "DWORD", "1", Profile.WindWebUI)],
            IniEntries = [new("WebUISupport", AppIni, "WebUI", "Support", "1", Profile.WindWebUI)],
        }));

        Registry.Seed(RegKey, "Support", RegistryValueType.DWord, "0");
        Services.Add("WIND", wind);

        if (importBuild)
        {
            Directory.CreateDirectory(Path.Combine(SourceBuild, "lib"));
            File.WriteAllText(Path.Combine(SourceBuild, "WIND.exe"), "new-exe");
            File.WriteAllText(Path.Combine(SourceBuild, "config.txt"), "cfg");
            File.WriteAllText(Path.Combine(SourceBuild, "lib", "x.dll"), "dll");
            var (build, errors) = Builds.Import("wind", "2.1.0", SourceBuild);
            Assert.True(build is not null, string.Join("; ", errors));
        }

        Planner = PlannerFor(Machine);
    }

    public MachineConfig MachineWith(string environment, string cacheDir) => new()
    {
        MachineName = "TEST-PC", Environment = environment, SourcePath = Path.Combine(Root, "machine.ini"),
        InstallLocations = new Dictionary<string, string> { ["TEST"] = Apps },
        LocalCacheDirectory = cacheDir,
    };

    /// <summary>Membuat PlanBuilder (dan Guards) untuk konfigurasi mesin tertentu, memakai fake yang sama.</summary>
    public PlanBuilder PlannerFor(MachineConfig machine)
    {
        Guards = new Guards(machine, Allow, Builds, () => [.. UnfinishedBackups], _ => FreeSpace);
        return new PlanBuilder(machine, Allow, Components, Profiles, Validator, Builds, Registry, Services, Processes, Guards);
    }

    private void WriteAllowList()
    {
        Directory.CreateDirectory(Path.Combine(Release, "Config"));
        string path = Paths.AllowListIni(Release);
        File.WriteAllText(path, $"""
            [Meta]
            SchemaVersion=1.0
            [BaseDirectories]
            TEST={Apps}
            [RegistryKeys]
            WebUI={RegKey}
            [IniFiles]
            WebUIUXCNFG={AppIni}
            [Services]
            WIND=WIND
            """);
        var (list, errors) = AllowList.Load(path);
        Assert.True(list is not null, string.Join("; ", errors));
        Allow = list;
    }

    public static PlanRequest All(string component = "wind", string version = "2.1.0") =>
        new("TESTP", component, version, DeployFiles: true, ApplyConfig: true, RestartService: false);

    /// <summary>Isi folder (path relatif -> isi file), untuk membandingkan sebelum/sesudah.</summary>
    public static SortedDictionary<string, string> SnapshotFolder(string dir) =>
        new(Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(dir, f), File.ReadAllText)
            : [], StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _junctions = [];

    /// <summary>Membuat junction (tanpa hak admin) dan mencatatnya agar dilepas sebelum folder uji dihapus.</summary>
    public void MakeJunction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, "mklink /J failed: " + process.StandardError.ReadToEnd());
        _junctions.Add(link);
    }

    public void Dispose()
    {
        // Junction dilepas dulu (tanpa recursive) supaya penghapusan folder uji tidak menyentuh isi tujuannya.
        foreach (var link in _junctions)
            if (Directory.Exists(link)) Directory.Delete(link, recursive: false);
        try { Directory.Delete(Root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* sisa folder temp tidak menggagalkan tes */ }
    }
}
