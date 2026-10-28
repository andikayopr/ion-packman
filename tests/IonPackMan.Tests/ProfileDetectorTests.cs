using IonPackMan.Components;
using IonPackMan.Machine;
using IonPackMan.Profiles;
using Xunit;

namespace IonPackMan.Tests;

public class ProfileDetectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));
    private readonly ComponentStore _components;
    private readonly ProfileStore _profiles;

    // Folder/service yang "ada" di mesin palsu.
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _services = new(StringComparer.OrdinalIgnoreCase);

    public ProfileDetectorTests()
    {
        Directory.CreateDirectory(_dir);
        _components = new ComponentStore(_dir);
        _profiles = new ProfileStore(_dir, new ProfileValidator(_components, TestAllowList.Create()));

        SaveComponent("ion-middleware", "1.8.2", @"D:\ION\middleware", "");
        SaveComponent("wind", "2.1.0", @"D:\ION\wind", "WIND");
        SaveComponent("qraptor", "1.3.1", @"D:\ION\qraptor", "");
        SaveComponent("non-ndc-parser", "1.4.1", @"D:\ION\parser", "");
        SaveComponent("mdd-service", "2.4.0", @"D:\ION\mdd", "");

        Assert.Empty(_profiles.Save(new Profile
        {
            Id = "QRISTAP", Version = "1.0.0",
            Components = [C("ion-middleware", "1.8.2"), C("wind", "2.1.0", "ion-middleware"), C("qraptor", "1.3.1", "ion-middleware")],
        }));
        Assert.Empty(_profiles.Save(new Profile
        {
            Id = "MDD", Version = "1.0.0",
            Components =
            [
                C("ion-middleware", "1.8.2"), C("wind", "2.1.0", "ion-middleware"),
                C("non-ndc-parser", "1.4.1", "ion-middleware"), C("mdd-service", "2.4.0", "ion-middleware", "wind", "non-ndc-parser"),
            ],
        }));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ProfileComponent C(string id, string version, params string[] deps) => new(id, version, deps);

    private void SaveComponent(string id, string version, string target, string service) =>
        Assert.Empty(_components.Save(new ComponentRelease { Id = id, ReleaseVersion = version, TargetDirectory = target, ServiceName = service }));

    private ProfileDetector Detector() => new(_components, _profiles, _dirs.Contains, _services.Contains);

    private static MachineConfig Machine(string profile = "") => new()
    {
        MachineName = "PC1", Environment = "Test", Profile = profile,
        InstallLocations = new Dictionary<string, string>(), LocalCacheDirectory = @"D:\Cache",
    };

    private void InstallQrisTap()
    {
        _dirs.UnionWith([@"D:\ION\middleware", @"D:\ION\wind", @"D:\ION\qraptor"]);
        _services.Add("WIND");
    }

    private void InstallMdd()
    {
        _dirs.UnionWith([@"D:\ION\middleware", @"D:\ION\wind", @"D:\ION\parser", @"D:\ION\mdd"]);
        _services.Add("WIND");
    }

    [Fact]
    public void MachineIniProfile_Existing_IsUsed_EvenIfNothingInstalled()
    {
        var r = Detector().Detect(Machine("MDD"));

        Assert.Equal("MDD", r.ProfileId);
        Assert.Equal("Selected in machine.ini", r.Reason);
    }

    [Fact]
    public void MachineIniProfile_Missing_IsUnknown_WithReason()
    {
        InstallQrisTap();

        var r = Detector().Detect(Machine("NOPE"));

        Assert.Null(r.ProfileId);
        Assert.Contains("NOPE", r.Reason);
    }

    [Fact]
    public void OnlyQrisTapInstalled_DetectsQrisTap()
    {
        InstallQrisTap();

        Assert.Equal("QRISTAP", Detector().Detect(Machine()).ProfileId);
    }

    [Fact]
    public void OnlyMddInstalled_DetectsMdd()
    {
        InstallMdd();

        Assert.Equal("MDD", Detector().Detect(Machine()).ProfileId);
    }

    [Fact]
    public void NothingInstalled_IsUnknown_NamesClosestAndMissing()
    {
        var r = Detector().Detect(Machine());

        Assert.Null(r.ProfileId);
        Assert.Contains("No profile matches", r.Reason);
        Assert.Contains("Closest is", r.Reason);
        Assert.Contains("not found", r.Reason);
    }

    [Fact]
    public void MissingService_PreventsMatch_AndIsNamed()
    {
        InstallQrisTap();
        _services.Clear();

        var r = Detector().Detect(Machine());

        Assert.Null(r.ProfileId);
        Assert.Contains("QRISTAP", r.Reason);
        Assert.Contains("service WIND not found", r.Reason);
    }

    [Fact]
    public void PartialInstall_ClosestIsTheProfileWithMostComponentsFound()
    {
        InstallQrisTap();
        _dirs.Remove(@"D:\ION\qraptor");

        var r = Detector().Detect(Machine());

        Assert.Null(r.ProfileId);
        Assert.Contains("Closest is QRISTAP (2 of 3", r.Reason);
        Assert.Contains("qraptor", r.Reason);
    }

    [Fact]
    public void MoreThanOneMatch_IsUnknown_ListsAll_AndSuggestsMachineIni()
    {
        // MDD dipasang di mesin yang sama dengan QRISTAP: keduanya cocok.
        InstallQrisTap();
        InstallMdd();

        var r = Detector().Detect(Machine());

        Assert.Null(r.ProfileId);
        Assert.Contains("QRISTAP", r.Reason);
        Assert.Contains("MDD", r.Reason);
        Assert.Contains("machine.ini", r.Reason);
    }

    [Fact]
    public void UnreadableProfile_IsUnknown_WithReason()
    {
        InstallQrisTap();
        File.WriteAllText(Paths.ProfileIni(_dir, "BROKEN"), "this is not an ini [");

        var r = Detector().Detect(Machine());

        Assert.Null(r.ProfileId);
        Assert.Contains("BROKEN", r.Reason);
    }

    [Fact]
    public void NoProfiles_IsUnknown()
    {
        string empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);
        var comps = new ComponentStore(empty);
        var d = new ProfileDetector(comps, new ProfileStore(empty, new ProfileValidator(comps, TestAllowList.Create())), _ => true, _ => true);

        var r = d.Detect(Machine());

        Assert.Null(r.ProfileId);
        Assert.Contains("No profiles", r.Reason);
    }
}
