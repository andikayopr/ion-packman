using IonPackMan.Profiles;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>Katalog global entri Registry/INI mesin ATM (Config\config-entries.ini).</summary>
public class ConfigCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-cc-" + Guid.NewGuid().ToString("N"));
    private string CatalogFile => Path.Combine(_dir, "config-entries.ini");

    public ConfigCatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Entries(string registryValue = "1") => $"""
        [Meta]
        SchemaVersion=1.0
        [Registry:WebUISupport]
        Key={TestAllowList.WebUiKey}
        ValueName=Support
        Type=DWORD
        Value={registryValue}
        Feature=WindWebUI
        [Ini:WebUIJnlLog]
        File={TestAllowList.WebUiIni}
        Section=Debug
        Key=JNLLog
        Value=1
        Feature=JnlLog
        """;

    private static Profile Bare(bool webUi = true, bool jnl = true) => new()
    {
        Id = "P", Version = "1.0.0", WindWebUIEnabled = webUi, JnlLogEnabled = jnl,
        Components = [new("ion-middleware", "1.8.2", []), new("wind", "2.1.0", ["ion-middleware"])],
    };

    [Fact]
    public void MissingFile_IsAnEmptyCatalog_NotAnError()
    {
        var (catalog, errors) = ConfigCatalog.Load(CatalogFile);

        Assert.Empty(errors);
        Assert.Empty(catalog.RegistryEntries);
        Assert.Empty(catalog.IniEntries);
    }

    [Fact]
    public void ValidFile_LoadsEntries_AndAProfileWithoutOwnEntriesStillSeesThem()
    {
        File.WriteAllText(CatalogFile, Entries());
        var (catalog, errors) = ConfigCatalog.Load(CatalogFile);
        Assert.Empty(errors);

        var rows = ConfigResolver.Resolve(Bare(), TestAllowList.Create(), catalog);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(EntryState.Enabled, r.State));
        Assert.All(rows, r => Assert.Null(r.AllowListProblem));
    }

    [Fact]
    public void ProfileFeatures_DecideWhichCatalogEntriesApply()
    {
        File.WriteAllText(CatalogFile, Entries());
        var (catalog, _) = ConfigCatalog.Load(CatalogFile);

        var rows = ConfigResolver.Resolve(Bare(webUi: false, jnl: false), TestAllowList.Create(), catalog);

        Assert.All(rows, r => Assert.Equal(EntryState.NotApplied, r.State));
    }

    [Fact]
    public void ProfileEntryWithTheSameName_OverridesTheCatalogEntry()
    {
        File.WriteAllText(CatalogFile, Entries("1"));
        var (catalog, _) = ConfigCatalog.Load(CatalogFile);
        var profile = Bare() with { RegistryEntries = [new("WebUISupport", TestAllowList.WebUiKey, "Support", "DWORD", "7", Profile.WindWebUI)] };

        var (registry, ini) = catalog.EntriesFor(profile);

        Assert.Equal("7", Assert.Single(registry).Value);
        Assert.Single(ini);
    }

    [Fact]
    public void EntryOutsideAllowList_MakesEveryProfileInvalid()
    {
        File.WriteAllText(CatalogFile, Entries().Replace(TestAllowList.WebUiIni, @"C:\Windows\win.ini"));
        var (catalog, errors) = ConfigCatalog.Load(CatalogFile);
        Assert.Empty(errors);   // bentuknya benar; izin diperiksa lewat allow-list

        var rows = ConfigResolver.Resolve(Bare(), TestAllowList.Create(), catalog);

        Assert.Contains(rows, r => r.AllowListProblem is not null);
    }

    [Theory]
    [InlineData("[Registry:Bad]\nKey=HKLM\\SOFTWARE\\X\nValueName=V\nType=Nope\n", "Type 'Nope'")]
    [InlineData("[Registry:Bad]\nKey=HKLM\\SOFTWARE\\X\nValueName=V\nType=DWORD\nValue=abc\n", "DWORD Value")]
    [InlineData("[Ini:Bad]\nFile=D:\\a.ini\nSection=S\nKey=K\nFeature=Nope\n", "Feature 'Nope'")]
    [InlineData("[Registry:Bad]\nKey=HKLM\\SOFTWARE\\X\n", "ValueName")]
    [InlineData("[Unknown]\nA=1\n", "Unknown")]
    public void BadFile_IsRejectedWithReasons_AndFallsBackToEmpty(string body, string expected)
    {
        File.WriteAllText(CatalogFile, "[Meta]\nSchemaVersion=1.0\n" + body);

        var (catalog, errors) = ConfigCatalog.Load(CatalogFile);

        Assert.Contains(expected, string.Join(" | ", errors));
        Assert.Empty(catalog.RegistryEntries);
        Assert.Empty(catalog.IniEntries);
    }
}
