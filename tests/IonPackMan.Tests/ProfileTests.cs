using IonPackMan.Components;
using IonPackMan.Profiles;
using Xunit;

namespace IonPackMan.Tests;

public class ProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileValidator _validator;
    private readonly ProfileStore _store;

    public ProfileTests()
    {
        Directory.CreateDirectory(_dir);
        var components = new ComponentStore(_dir);
        foreach (var (id, version) in new[]
        {
            ("ion-middleware", "1.8.2"), ("wind", "2.1.0"), ("qraptor", "1.3.1"),
            ("non-ndc-parser", "1.4.1"), ("mdd-service", "2.4.0"),
        })
        {
            Assert.Empty(components.Save(new ComponentRelease { Id = id, ReleaseVersion = version, TargetDirectory = @"D:\ION\" + id }));
        }
        _validator = new ProfileValidator(components, TestAllowList.Create());
        _store = new ProfileStore(_dir, _validator);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ProfileComponent C(string id, string version, params string[] deps) => new(id, version, deps);

    private static Profile QrisTap() => new()
    {
        Id = "QRISTAP", DisplayName = "QRIS TAP", Version = "1.0.0",
        WindWebUIEnabled = true,
        Components = [C("ion-middleware", "1.8.2"), C("wind", "2.1.0", "ion-middleware"), C("qraptor", "1.3.1", "ion-middleware")],
        RegistryEntries = [new("WebUISupport", @"HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI", "Support", "DWORD", "1", Profile.WindWebUI)],
        IniEntries = [new("WebUIJnlLog", @"D:\APCONTAINER\Config\WebUIUXCNFG.ini", "Debug", "JNLLog", "1", Profile.JnlLog)],
    };

    private static Profile Mdd() => new()
    {
        Id = "MDD", DisplayName = "MDD", Version = "1.0.0",
        Components =
        [
            C("ion-middleware", "1.8.2"),
            C("wind", "2.1.0", "ion-middleware"),
            C("non-ndc-parser", "1.4.1", "ion-middleware"),
            C("mdd-service", "2.4.0", "ion-middleware", "wind", "non-ndc-parser"),   // D2
        ],
    };

    // ---------- R8 ----------

    [Fact]
    public void BothDocumentedProfiles_AreValid()
    {
        Assert.Empty(_validator.Validate(QrisTap()));
        Assert.Empty(_validator.Validate(Mdd()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("QRIS TAP")]
    [InlineData("..")]
    [InlineData("a\\b")]
    public void InvalidProfileId_IsReported(string id)
    {
        Assert.Contains(_validator.Validate(QrisTap() with { Id = id }), p => p.Contains("Profile Id"));
    }

    [Fact]
    public void NoComponents_IsReported()
    {
        Assert.Contains("Profile has no components.", _validator.Validate(QrisTap() with { Components = [], WindWebUIEnabled = false, RegistryEntries = [], IniEntries = [] }));
    }

    [Fact]
    public void DependsOnComponentNotInProfile_IsReported()
    {
        var p = QrisTap() with { Components = [C("wind", "2.1.0", "ion-middleware")] };

        Assert.Contains(_validator.Validate(p), m => m.Contains("wind depends on 'ion-middleware', which is not in this profile"));
    }

    [Fact]
    public void SelfDependency_IsReported()
    {
        var p = QrisTap() with { Components = [C("ion-middleware", "1.8.2", "ion-middleware"), C("wind", "2.1.0")] };

        Assert.Contains(_validator.Validate(p), m => m.Contains("ion-middleware depends on itself"));
    }

    [Fact]
    public void CircularDependency_IsReported_WithThePath()
    {
        var two = QrisTap() with { Components = [C("ion-middleware", "1.8.2", "wind"), C("wind", "2.1.0", "ion-middleware")] };
        var three = Mdd() with
        {
            Components = [C("ion-middleware", "1.8.2", "mdd-service"), C("wind", "2.1.0", "ion-middleware"), C("mdd-service", "2.4.0", "wind")],
        };

        Assert.Contains(_validator.Validate(two), m => m.StartsWith("Circular dependency:") && m.Contains("ion-middleware → wind → ion-middleware"));
        Assert.Contains(_validator.Validate(three), m => m.StartsWith("Circular dependency:"));
    }

    [Fact]
    public void DiamondDependencies_AreNotACycle()
    {
        // mdd-service -> wind -> ion-middleware dan mdd-service -> ion-middleware (jalur ganda, bukan siklus)
        Assert.DoesNotContain(_validator.Validate(Mdd()), m => m.Contains("Circular"));
    }

    [Fact]
    public void JnlLogWithoutWebUI_IsReported()
    {
        var p = QrisTap() with { WindWebUIEnabled = false, JnlLogEnabled = true };

        Assert.Contains("JnlLog requires WindWebUI to be enabled.", _validator.Validate(p));
    }

    [Fact]
    public void WebUIWithoutWind_IsReported()
    {
        var p = QrisTap() with { Components = [C("ion-middleware", "1.8.2")] };

        Assert.Contains(_validator.Validate(p), m => m.Contains("WindWebUI is enabled but the profile has no 'wind' component"));
    }

    [Fact]
    public void ComponentVersionNotSavedInComponents_IsReported()
    {
        var p = QrisTap() with { Components = [C("ion-middleware", "1.8.2"), C("wind", "9.9.9", "ion-middleware")] };

        Assert.Contains("wind@9.9.9 is not saved in Components.", _validator.Validate(p));
    }

    [Fact]
    public void DuplicateComponent_IsReported()
    {
        var p = QrisTap() with { Components = [C("ion-middleware", "1.8.2"), C("wind", "2.1.0"), C("wind", "2.1.0")] };

        Assert.Contains("Component 'wind' is listed more than once.", _validator.Validate(p));
    }

    [Fact]
    public void AllProblemsAreReportedTogether()
    {
        var p = new Profile { Id = "", Version = "", Components = [C("wind", "9.9.9", "ghost")], JnlLogEnabled = true };

        Assert.True(_validator.Validate(p).Count >= 5);
    }

    [Theory]
    [InlineData("Binary", "1", "Type 'Binary'")]
    [InlineData("DWORD", "abc", "DWORD Value 'abc'")]
    [InlineData("DWORD", "-1", "DWORD Value '-1'")]
    [InlineData("DWORD", "4294967296", "DWORD Value")]
    public void RegistryEntryShape_IsChecked(string type, string value, string expected)
    {
        var p = QrisTap() with { RegistryEntries = [new("X", @"HKLM\SOFTWARE\X", "V", type, value, "")] };

        Assert.Contains(_validator.Validate(p), m => m.Contains(expected));
    }

    [Fact]
    public void EntryWithUnknownFeature_IsReported()
    {
        var p = QrisTap() with { IniEntries = [new("X", @"D:\a.ini", "S", "K", "1", "Nope")] };

        Assert.Contains(_validator.Validate(p), m => m.Contains("Feature 'Nope' is not known"));
    }

    [Fact]
    public void IsFeatureEnabled_FollowsTheToggles()
    {
        var p = QrisTap() with { JnlLogEnabled = false };

        Assert.True(p.IsFeatureEnabled(""));
        Assert.True(p.IsFeatureEnabled(Profile.WindWebUI));
        Assert.False(p.IsFeatureEnabled(Profile.JnlLog));
        Assert.False(p.IsFeatureEnabled("Unknown"));
    }

    [Fact]
    public void JnlLog_IsNeverEffectiveWithoutWebUI()
    {
        var p = QrisTap() with { WindWebUIEnabled = false, JnlLogEnabled = true };

        Assert.False(p.IsFeatureEnabled(Profile.JnlLog));
    }

    [Fact]
    public void WithWindWebUI_TurningItOff_TurnsJnlLogOffToo()
    {
        var on = QrisTap() with { JnlLogEnabled = true };

        var off = on.WithWindWebUI(false);
        Assert.False(off.WindWebUIEnabled);
        Assert.False(off.JnlLogEnabled);

        // Menyalakan lagi tidak menyalakan JnlLog diam-diam.
        Assert.False(off.WithWindWebUI(true).JnlLogEnabled);
        Assert.Empty(_validator.Validate(off));
    }

    [Fact]
    public void EntryOutsideAllowList_MakesTheProfileInvalid_EvenWhenItsFeatureIsOff()
    {
        var badKey = QrisTap() with { RegistryEntries = [new("X", @"HKLM\SOFTWARE\Microsoft\Windows", "V", "DWORD", "1", Profile.JnlLog)], JnlLogEnabled = false };
        var badIni = QrisTap() with { IniEntries = [new("Y", @"C:\Windows\win.ini", "S", "K", "1", "")] };

        Assert.Contains(_validator.Validate(badKey), m => m.StartsWith("Registry:X:") && m.Contains("not in the allow-list"));
        Assert.Contains(_validator.Validate(badIni), m => m.StartsWith("Ini:Y:") || m.StartsWith("INI:Y:"));
    }

    [Fact]
    public void Save_ProfileWithEntryOutsideAllowList_WritesNothing()
    {
        var bad = QrisTap() with { RegistryEntries = [new("X", @"HKLM\SOFTWARE\Microsoft", "V", "DWORD", "1", "")] };

        Assert.NotEmpty(_store.Save(bad));
        Assert.Empty(_store.ListIds());
    }

    // ---------- Store ----------

    [Fact]
    public void SaveThenLoad_RoundTripsEverything()
    {
        var saved = QrisTap() with { JnlLogEnabled = true };

        Assert.Empty(_store.Save(saved));
        var (loaded, errors) = _store.Load("QRISTAP");

        Assert.Empty(errors);
        Assert.Equal(saved.Id, loaded!.Id);
        Assert.Equal(saved.DisplayName, loaded.DisplayName);
        Assert.Equal(saved.Components.Select(c => (c.Id, c.Version, string.Join(",", c.DependsOn))),
                     loaded.Components.Select(c => (c.Id, c.Version, string.Join(",", c.DependsOn))));
        Assert.Equal(saved.RegistryEntries, loaded.RegistryEntries);
        Assert.Equal(saved.IniEntries, loaded.IniEntries);
        Assert.True(loaded.WindWebUIEnabled);
        Assert.True(loaded.JnlLogEnabled);
        Assert.Empty(_validator.Validate(loaded));
    }

    [Fact]
    public void Save_InvalidProfile_WritesNothing()
    {
        var problems = _store.Save(QrisTap() with { JnlLogEnabled = true, WindWebUIEnabled = false });

        Assert.NotEmpty(problems);
        Assert.Empty(_store.ListIds());
    }

    [Fact]
    public void ListIds_ReturnsSavedProfilesSorted_IgnoringStrayFiles()
    {
        _store.Save(Mdd());
        _store.Save(QrisTap());
        File.WriteAllText(Path.Combine(_dir, "Profiles", "readme.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "Profiles", "bad name.ini"), "x");

        Assert.Equal(new[] { "MDD", "QRISTAP" }, _store.ListIds());
    }

    [Fact]
    public void Load_UnsafeId_IsRejected_BeforeTouchingTheDisk()
    {
        var (profile, errors) = _store.Load(@"..\..\evil");

        Assert.Null(profile);
        Assert.Contains("not a valid profile Id", Assert.Single(errors));
    }

    [Fact]
    public void Load_FileDeclaringAnotherId_IsRejected()
    {
        _store.Save(Mdd());
        File.Copy(Paths.ProfileIni(_dir, "MDD"), Paths.ProfileIni(_dir, "COPY"));

        var (profile, errors) = _store.Load("COPY");

        Assert.Null(profile);
        Assert.Contains("declares profile 'MDD'", Assert.Single(errors));
    }

    [Fact]
    public void Load_ParsesPrdExample_IncludingTrailingComments()
    {
        string path = Paths.ProfileIni(_dir, "QRISTAP");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            [Meta]
            SchemaVersion=1.0
            [Profile]
            Id=QRISTAP
            DisplayName=QRIS TAP
            Version=1.0.0
            DeploymentMode=in-place
            [Features]
            WindWebUI=true
            JnlLog=true                    ; hanya berlaku jika WindWebUI=true
            [Component:ion-middleware]
            Version=1.8.2
            DependsOn=
            [Component:wind]
            Version=2.1.0
            DependsOn=ion-middleware
            [Registry:WebUISupport]
            Key=HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI
            ValueName=Support
            Type=DWORD
            Value=1
            Feature=WindWebUI
            [Ini:WebUIJnlLog]
            File=D:\APCONTAINER\Config\WebUIUXCNFG.ini
            Section=Debug
            Key=JNLLog
            Value=1
            Feature=JnlLog
            """);

        var (profile, errors) = _store.Load("QRISTAP");

        Assert.Empty(errors);
        Assert.True(profile!.JnlLogEnabled);
        Assert.Equal(new[] { "ion-middleware", "wind" }, profile.Components.Select(c => c.Id));
        Assert.Equal(new[] { "ion-middleware" }, profile.Components[1].DependsOn);
        Assert.Equal("WebUISupport", profile.RegistryEntries.Single().Name);
        Assert.Equal("JNLLog", profile.IniEntries.Single().Key);
        Assert.Empty(_validator.Validate(profile));
    }

    [Fact]
    public void Load_BadFile_ReportsStructureProblems()
    {
        string path = Paths.ProfileIni(_dir, "BAD");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            [Meta]
            SchemaVersion=1.0
            [Profile]
            Id=BAD
            [Features]
            WindWebUI=maybe
            Colour=red
            [Component:wind]
            DependsOn=
            [Registry:X]
            Key=HKLM\x
            [Surprise]
            A=1
            """);

        var (profile, errors) = _store.Load("BAD");

        Assert.Null(profile);
        Assert.Contains(errors, e => e.Contains("[Profile] is missing required key 'Version'"));
        Assert.Contains(errors, e => e.Contains("WindWebUI must be true or false"));
        Assert.Contains(errors, e => e.Contains("unknown key 'Colour'"));
        Assert.Contains(errors, e => e.Contains("[Component:wind] is missing required key 'Version'"));
        Assert.Contains(errors, e => e.Contains("[Registry:X] is missing required key 'ValueName'"));
        Assert.Contains(errors, e => e.Contains("unknown section [Surprise]"));
    }

    [Fact]
    public void Import_InvalidProfile_IsNotSaved()
    {
        string external = Path.Combine(_dir, "external.ini");
        (Mdd() with { Components = [C("mdd-service", "2.4.0", "wind")] }).ToIni().Save(external);

        var (profile, errors) = _store.Import(external);

        Assert.Null(profile);
        Assert.NotEmpty(errors);
        Assert.Empty(_store.ListIds());
    }

    [Fact]
    public void Import_ValidProfile_IsSaved()
    {
        string external = Path.Combine(_dir, "external.ini");
        Mdd().ToIni().Save(external);

        var (profile, errors) = _store.Import(external);

        Assert.Empty(errors);
        Assert.Equal("MDD", profile!.Id);
        Assert.Equal(new[] { "MDD" }, _store.ListIds());
    }
}
