using IonPackMan.Components;
using IonPackMan.Machine;
using IonPackMan.Profiles;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>Data awal yang ikut ke folder rilis (src/IonPackMan/Data) harus valid menurut aturan aplikasi sendiri.</summary>
public class SeedDataTests
{
    private static readonly string ReleaseDir = AppContext.BaseDirectory;

    private static AllowList LoadAllowList()
    {
        var (list, errors) = AllowList.Load(Paths.AllowListIni(ReleaseDir));
        Assert.Empty(errors);
        return list!;
    }

    [Fact]
    public void AllowList_Loads_AndCoversEverythingTheSeedUses()
    {
        var allow = LoadAllowList();

        Assert.True(allow.IsUnderBaseDirectory(@"D:\APCONTAINER\WIND"));
        Assert.True(allow.IsServiceAllowed("MddService"));
        Assert.True(allow.IsIniFileAllowed(@"D:\APCONTAINER\Config\GPFCNFG.ini"));
        Assert.True(allow.IsRegistryKeyAllowed(@"HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\Online_OPL\Welcome\LocalScreen"));
        Assert.False(allow.IsUnderBaseDirectory(@"C:\Windows"));
    }

    [Fact]
    public void Catalog_ListsTheFiveInitialComponents()
    {
        var (entries, errors) = new ComponentStore(ReleaseDir).LoadCatalog();

        Assert.Empty(errors);
        Assert.Equal(new[] { "ion-middleware", "wind", "qraptor", "non-ndc-parser", "mdd-service" }, entries.Select(e => e.Id));
    }

    [Fact]
    public void EveryCatalogComponent_HasAValidSavedRelease_InsideTheAllowList()
    {
        var store = new ComponentStore(ReleaseDir);
        var allow = LoadAllowList();

        foreach (var entry in store.LoadCatalog().Entries)
        {
            var versions = store.ListVersions(entry.Id);
            Assert.True(versions.Count > 0, $"{entry.Id} has no saved release.");
            foreach (string version in versions)
            {
                var (release, errors) = store.Load(entry.Id, version);
                Assert.True(errors.Count == 0, string.Join("; ", errors));
                Assert.Empty(release!.Validate(allow));
            }
        }
    }

    private static (ProfileStore Store, ProfileValidator Validator) Profiles()
    {
        var validator = new ProfileValidator(new ComponentStore(ReleaseDir), LoadAllowList());
        return (new ProfileStore(ReleaseDir, validator), validator);
    }

    [Fact]
    public void BothProfiles_AreValid()
    {
        var (store, validator) = Profiles();

        Assert.Equal(new[] { "MDD", "QRISTAP" }, store.ListIds());
        foreach (string id in store.ListIds())
        {
            var (profile, errors) = store.Load(id);
            Assert.True(errors.Count == 0, string.Join("; ", errors));
            Assert.Empty(validator.Validate(profile!));
        }
    }

    [Fact]
    public void MddProfile_FollowsDecisionD2()
    {
        var mdd = Profiles().Store.Load("MDD").Profile!;
        var deps = mdd.Components.ToDictionary(c => c.Id, c => c.DependsOn.ToArray());

        Assert.Equal(new[] { "ion-middleware" }, deps["wind"]);
        Assert.Equal(new[] { "ion-middleware" }, deps["non-ndc-parser"]);
        Assert.Equal(new[] { "ion-middleware", "wind", "non-ndc-parser" }, deps["mdd-service"]);
    }

    [Fact]
    public void QrisTap_AppliesAllSixEntries_AndMddAppliesNone()
    {
        var allow = LoadAllowList();
        var store = Profiles().Store;

        var qris = ConfigResolver.Resolve(store.Load("QRISTAP").Profile!, allow);
        var mdd = ConfigResolver.Resolve(store.Load("MDD").Profile!, allow);

        Assert.Equal(6, qris.Count);
        Assert.All(qris, e => Assert.Equal(EntryState.Enabled, e.State));
        Assert.Equal(2, qris.Count(e => e.Scope == "Registry"));
        Assert.Equal(4, qris.Count(e => e.Scope == "INI"));

        Assert.Equal(6, mdd.Count);
        Assert.All(mdd, e => Assert.Equal(EntryState.NotApplied, e.State));
    }
}
