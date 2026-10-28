using IonPackMan.Profiles;
using Xunit;

namespace IonPackMan.Tests;

public class ConfigResolverTests
{
    private static readonly ProfileComponent Middleware = new("ion-middleware", "1.8.2", []);
    private static ProfileComponent Wind(string version) => new("wind", version, ["ion-middleware"]);

    // Entri seperti di PRD §7.2: Registry WebUI (fitur WindWebUI) dan INI JNLLog (fitur JnlLog).
    private static Profile Qris(string windVersion = "2.1.0", bool webUi = true, bool jnl = true) => new()
    {
        Id = "QRISTAP", Version = "1.0.0", WindWebUIEnabled = webUi, JnlLogEnabled = jnl,
        Components = [Middleware, Wind(windVersion)],
        RegistryEntries =
        [
            new("WebUISupport", TestAllowList.WebUiKey, "Support", "DWORD", "1", Profile.WindWebUI),
            new("AlwaysOn", TestAllowList.WebUiKey, "Mode", "String", "x", ""),
        ],
        IniEntries = [new("WebUIJnlLog", TestAllowList.WebUiIni, "Debug", "JNLLog", "1", Profile.JnlLog)],
    };

    private static List<ResolvedEntry> Resolve(Profile p) => ConfigResolver.Resolve(p, TestAllowList.Create());

    [Fact]
    public void AllFeaturesOn_AllEntriesAreEnabled()
    {
        var entries = Resolve(Qris());

        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal(EntryState.Enabled, e.State));
        Assert.All(entries, e => Assert.Null(e.AllowListProblem));
    }

    [Fact]
    public void RowsCarryWhatTheTableNeeds()
    {
        var entries = Resolve(Qris());

        var reg = entries[0];
        Assert.Equal(("Registry", "WebUISupport", TestAllowList.WebUiKey, "Support", "DWORD", "1"),
                     (reg.Scope, reg.Name, reg.Target, reg.Entry, reg.Type, reg.Value));

        var ini = entries[2];
        Assert.Equal(("INI", "WebUIJnlLog", TestAllowList.WebUiIni, "[Debug] JNLLog", "", "1"),
                     (ini.Scope, ini.Name, ini.Target, ini.Entry, ini.Type, ini.Value));
    }

    [Fact]
    public void WebUIOff_NotApplies_WebUIEntries_AndJnlLogTooEvenIfItsToggleIsOn()
    {
        var entries = Resolve(Qris(webUi: false, jnl: true));

        Assert.Equal(EntryState.NotApplied, entries.Single(e => e.Name == "WebUISupport").State);
        Assert.Equal(EntryState.NotApplied, entries.Single(e => e.Name == "WebUIJnlLog").State);
        Assert.Equal(EntryState.Enabled, entries.Single(e => e.Name == "AlwaysOn").State);   // tanpa fitur: selalu berlaku
    }

    [Fact]
    public void JnlLogOff_OnlyJnlLogEntriesAreNotApplied()
    {
        var entries = Resolve(Qris(jnl: false));

        Assert.Equal(new[] { "WebUIJnlLog" }, entries.Where(e => e.State == EntryState.NotApplied).Select(e => e.Name));
    }

    [Fact]
    public void EntriesOutsideTheAllowList_AreMarkedWithAReason()
    {
        var p = Qris() with
        {
            RegistryEntries = [new("Bad", @"HKLM\SOFTWARE\Microsoft\Windows", "V", "DWORD", "1", "")],
            IniEntries = [new("BadIni", @"C:\Windows\win.ini", "S", "K", "1", "")],
        };

        var entries = Resolve(p);

        Assert.Contains("Registry key", entries[0].AllowListProblem);
        Assert.Contains("INI file", entries[1].AllowListProblem);
    }

    [Fact]
    public void Result_IsTheSame_ForDifferentWindVersions()
    {
        // R7: entri berlaku untuk Profile, terlepas dari versi WIND yang dipilih.
        Assert.Equal(Resolve(Qris("2.1.0")), Resolve(Qris("3.0.0-dev.5")));
    }

    [Fact]
    public void NoEntries_GivesAnEmptyList()
    {
        Assert.Empty(Resolve(Qris() with { RegistryEntries = [], IniEntries = [] }));
    }
}
