using IonPackMan.Components;
using IonPackMan.Ini;
using IonPackMan.Machine;
using Xunit;

namespace IonPackMan.Tests;

public class ComponentStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));
    private readonly ComponentStore _store;

    public ComponentStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new ComponentStore(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ComponentRelease Wind(string version = "2.1.0") => new()
    {
        Id = "wind", DisplayName = "WIND", ReleaseVersion = version,
        TargetDirectory = @"D:\APCONTAINER\WIND", ServiceName = "WIND",
    };

    private static AllowList Allow()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "ionpackman-allow-" + Guid.NewGuid().ToString("N") + ".ini");
        File.WriteAllText(tmp, "[Meta]\nSchemaVersion=1.0\n[BaseDirectories]\nION=D:\\ION\nAPC=D:\\APCONTAINER\n[Services]\nWIND=WIND\n");
        try { return AllowList.Load(tmp).List!; }
        finally { File.Delete(tmp); }
    }

    // ---------- Save / Load ----------

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var saved = Wind() with { BuildReference = "ci-1234" };

        Assert.Empty(_store.Save(saved));
        var (loaded, errors) = _store.Load("wind", "2.1.0");

        Assert.Empty(errors);
        Assert.Equal(saved, loaded);
        Assert.True(File.Exists(Paths.ComponentIni(_dir, "wind", "2.1.0")));
    }

    [Fact]
    public void Save_NewVersion_DoesNotTouchOtherVersions()
    {
        _store.Save(Wind("2.1.0"));
        string before = File.ReadAllText(Paths.ComponentIni(_dir, "wind", "2.1.0"));

        _store.Save(Wind("2.2.0") with { TargetDirectory = @"D:\APCONTAINER\WIND2" });

        Assert.Equal(before, File.ReadAllText(Paths.ComponentIni(_dir, "wind", "2.1.0")));
        Assert.Equal(@"D:\APCONTAINER\WIND2", _store.Load("wind", "2.2.0").Release!.TargetDirectory);
    }

    [Fact]
    public void Save_SameVersionAgain_UpdatesThatRelease()
    {
        _store.Save(Wind());
        _store.Save(Wind() with { BuildReference = "second" });

        Assert.Equal("second", _store.Load("wind", "2.1.0").Release!.BuildReference);
    }

    [Fact]
    public void Save_ServiceNameOptional_ForComponentsWithoutService()
    {
        var qraptor = new ComponentRelease { Id = "qraptor", ReleaseVersion = "1.3.1", TargetDirectory = @"D:\ION\QRAPTOR" };

        Assert.Empty(_store.Save(qraptor));
        Assert.Equal("", _store.Load("qraptor", "1.3.1").Release!.ServiceName);
    }

    // ---------- Validation ----------

    [Theory]
    [InlineData("Wind", "2.1.0")]            // huruf besar
    [InlineData("wind", "")]
    [InlineData("wind", "2.1.0/../x")]       // path traversal lewat versi
    [InlineData("..", "1.0")]
    [InlineData("a@b", "1.0")]               // '@' dipakai untuk folder cache
    [InlineData("wind", "2 1")]
    [InlineData("", "1.0")]
    [InlineData("wind-", "1.0")]
    public void Save_RejectsUnsafeIdOrVersion_AndWritesNothing(string id, string version)
    {
        var problems = _store.Save(new ComponentRelease { Id = id, ReleaseVersion = version, TargetDirectory = @"D:\ION\x" });

        Assert.NotEmpty(problems);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Theory]
    [InlineData("")]
    [InlineData("WIND")]                     // relatif
    [InlineData(@"\\server\share\WIND")]     // UNC
    public void Save_RejectsBadTargetDirectory(string target)
    {
        Assert.NotEmpty(_store.Save(Wind() with { TargetDirectory = target }));
    }

    [Fact]
    public void Save_RejectsUnknownMode()
    {
        Assert.Contains(_store.Save(Wind() with { Mode = "side-by-side" }), p => p.Contains("Mode"));
    }

    [Fact]
    public void Save_WithAllowList_RejectsTargetOutsideBaseAndUnlistedService()
    {
        var allow = Allow();

        Assert.Empty(_store.Save(Wind(), allow));

        var outside = _store.Save(Wind("3.0.0") with { TargetDirectory = @"C:\Windows\System32" }, allow);
        Assert.Contains(outside, p => p.Contains("not inside an allowed base directory"));

        var service = _store.Save(Wind("3.0.1") with { ServiceName = "Spooler" }, allow);
        Assert.Contains(service, p => p.Contains("not in the allow-list"));

        Assert.False(File.Exists(Paths.ComponentIni(_dir, "wind", "3.0.0")));
        Assert.False(File.Exists(Paths.ComponentIni(_dir, "wind", "3.0.1")));
    }

    [Fact]
    public void Save_ValueThatCannotBeStored_ReturnsMessageInsteadOfThrowing()
    {
        var problems = _store.Save(Wind() with { BuildReference = "ci ; build" });

        Assert.Contains("BuildReference", Assert.Single(problems));
    }

    // ---------- Load ----------

    [Fact]
    public void Load_FileDeclaringDifferentIdOrVersion_IsRejected()
    {
        _store.Save(Wind("2.1.0"));
        File.Copy(Paths.ComponentIni(_dir, "wind", "2.1.0"), Paths.ComponentIni(_dir, "wind", "9.9.9"));

        var (release, errors) = _store.Load("wind", "9.9.9");

        Assert.Null(release);
        Assert.Contains("declares wind@2.1.0", Assert.Single(errors));
    }

    [Fact]
    public void Load_MissingRelease_ReturnsErrorNotException()
    {
        var (release, errors) = _store.Load("wind", "0.0.1");

        Assert.Null(release);
        Assert.Contains("file not found", Assert.Single(errors));
    }

    [Fact]
    public void Load_BadFile_ReportsAllProblems()
    {
        string path = Paths.ComponentIni(_dir, "wind", "1.0.0");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[Meta]\nSchemaVersion=1.0\n[Component]\nId=wind\nColour=red\n[Surprise]\nX=1\n");

        var (release, errors) = _store.Load("wind", "1.0.0");

        Assert.Null(release);
        Assert.Contains(errors, e => e.Contains("missing required key 'ReleaseVersion'"));
        Assert.Contains(errors, e => e.Contains("unknown key 'Colour'"));
        Assert.Contains(errors, e => e.Contains("unknown section [Surprise]"));
        Assert.Contains(errors, e => e.Contains("missing section [Installation]"));
    }

    [Fact]
    public void Load_AcceptsPrdExample_WithTrailingComments()
    {
        string path = Paths.ComponentIni(_dir, "wind", "2.1.0");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            [Meta]
            SchemaVersion=1.0
            [Component]
            Id=wind
            DisplayName=WIND
            ReleaseVersion=2.1.0
            BuildReference=
            [Installation]
            Mode=in-place
            TargetDirectory=D:\APCONTAINER\WIND   ; target
            [WindowsService]
            ServiceName=WIND
            """);

        var (release, errors) = _store.Load("wind", "2.1.0");

        Assert.Empty(errors);
        Assert.Equal(Wind(), release);
    }

    // ---------- ListVersions ----------

    [Fact]
    public void ListVersions_NewestFirst_NumericAware_IgnoresOtherFiles()
    {
        foreach (var v in new[] { "2.9.0", "2.10.0", "2.1.0", "2.10.0-dev.1", "1.0.0" })
            _store.Save(Wind(v));
        File.WriteAllText(Path.Combine(_dir, "Components", "wind", "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "Components", "wind", "wind-bad name.ini"), "x");

        Assert.Equal(new[] { "2.10.0", "2.10.0-dev.1", "2.9.0", "2.1.0", "1.0.0" }, _store.ListVersions("wind"));
    }

    [Fact]
    public void ListVersions_UnknownOrUnsafeComponent_IsEmpty()
    {
        Assert.Empty(_store.ListVersions("wind"));
        Assert.Empty(_store.ListVersions(".."));
    }

    // ---------- Import ----------

    [Fact]
    public void Import_FileForAnotherComponent_IsRejected()
    {
        string external = Path.Combine(_dir, "external.ini");
        Wind().ToIni().Save(external);

        var (release, errors) = _store.Import(external, expectedComponentId: "ion-middleware");

        Assert.Null(release);
        Assert.Contains("for 'wind', not 'ion-middleware'", Assert.Single(errors));
        Assert.Empty(_store.ListVersions("wind"));
    }

    [Fact]
    public void Import_MatchingFile_IsSavedUnderItsOwnVersion()
    {
        string external = Path.Combine(_dir, "external.ini");
        Wind("4.0.0").ToIni().Save(external);

        var (release, errors) = _store.Import(external, "wind");

        Assert.Empty(errors);
        Assert.Equal("4.0.0", release!.ReleaseVersion);
        Assert.Equal(new[] { "4.0.0" }, _store.ListVersions("wind"));
    }

    // ---------- Catalog ----------

    [Fact]
    public void LoadCatalog_ReadsIdsAndNames()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Components"));
        File.WriteAllText(Paths.CatalogIni(_dir), "[Meta]\nSchemaVersion=1.0\n[Components]\nion-middleware=ION Middleware\nwind=WIND\nqraptor=\n");

        var (entries, errors) = _store.LoadCatalog();

        Assert.Empty(errors);
        Assert.Equal(new[] { "ion-middleware", "wind", "qraptor" }, entries.Select(e => e.Id));
        Assert.Equal("ION Middleware", entries[0].DisplayName);
        Assert.Equal("qraptor", entries[2].DisplayName);   // nama kosong -> pakai Id
    }

    [Fact]
    public void LoadCatalog_MissingFile_OrBadId_IsReported()
    {
        Assert.Contains("file not found", Assert.Single(_store.LoadCatalog().Errors));

        Directory.CreateDirectory(Path.Combine(_dir, "Components"));
        File.WriteAllText(Paths.CatalogIni(_dir), "[Meta]\nSchemaVersion=1.0\n[Components]\n..\\evil=x\n");
        Assert.Contains(_store.LoadCatalog().Errors, e => e.Contains("not a valid component Id"));
    }
}
