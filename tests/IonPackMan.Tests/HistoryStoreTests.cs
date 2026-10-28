using IonPackMan.History;
using Xunit;

namespace IonPackMan.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));
    private readonly HistoryStore _store;

    public HistoryStoreTests() => _store = new HistoryStore(_cache);

    public void Dispose()
    {
        if (Directory.Exists(_cache))
            Directory.Delete(_cache, recursive: true);
    }

    private static readonly DateTimeOffset Base = new(2026, 10, 28, 10, 15, 30, TimeSpan.FromHours(7));

    private static HistoryRecord Rec(string id, int minutes = 0, HistoryOutcome outcome = HistoryOutcome.Succeeded) => new()
    {
        Id = id, StartedAt = Base.AddMinutes(minutes), FinishedAt = Base.AddMinutes(minutes).AddSeconds(72),
        Operator = @"ATM-DEV-001\developer", Machine = "ATM-DEV-001", Profile = "QRISTAP", Component = "wind",
        BuildVersion = "2.1.2-dev.17", Operations = ["DeployFiles", "ApplyConfig"], Outcome = outcome,
        BackupDirectory = @"D:\ION-PackMan\Cache\Backups\" + id,
    };

    [Fact]
    public void WriteThenGetAll_RoundTripsEveryField()
    {
        var saved = Rec("20261028-101530-wind-3f9a", outcome: HistoryOutcome.FailedRestoreIncomplete) with
        {
            ErrorMessage = "Copy failed.", RestoreFailures = [@"D:\APCONTAINER\WIND\a.dll", @"D:\APCONTAINER\WIND\b.dll"],
        };

        Assert.Empty(_store.Write(saved));
        var (records, problems) = _store.GetAll();

        Assert.Empty(problems);
        var loaded = Assert.Single(records);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(saved.StartedAt, loaded.StartedAt);
        Assert.Equal(saved.StartedAt.Offset, loaded.StartedAt.Offset);
        Assert.Equal(saved.FinishedAt, loaded.FinishedAt);
        Assert.Equal(saved.Operator, loaded.Operator);
        Assert.Equal(saved.Machine, loaded.Machine);
        Assert.Equal(saved.Profile, loaded.Profile);
        Assert.Equal(saved.Component, loaded.Component);
        Assert.Equal(saved.BuildVersion, loaded.BuildVersion);
        Assert.Equal(saved.Operations, loaded.Operations);
        Assert.Equal(HistoryOutcome.FailedRestoreIncomplete, loaded.Outcome);
        Assert.Equal(saved.BackupDirectory, loaded.BackupDirectory);
        Assert.Equal("Copy failed.", loaded.ErrorMessage);
        Assert.Equal(saved.RestoreFailures, loaded.RestoreFailures);
    }

    [Fact]
    public void Write_OptionalFieldsMayBeEmpty_AndErrorSectionIsOmittedWhenNotFailed()
    {
        var minimal = new HistoryRecord
        {
            Id = "20261028-090000-wind-0001", StartedAt = Base, Operator = "op", Machine = "m", Component = "wind",
            Outcome = HistoryOutcome.Rejected,
        };

        Assert.Empty(_store.Write(minimal));
        Assert.DoesNotContain("[Error]", File.ReadAllText(Paths.HistoryIni(_cache, minimal.Id)));

        var loaded = Assert.Single(_store.GetAll().Records);
        Assert.Null(loaded.FinishedAt);
        Assert.Empty(loaded.Operations);
        Assert.Equal("", loaded.Profile);
        Assert.Equal("", loaded.ErrorMessage);
        Assert.Empty(loaded.RestoreFailures);
    }

    [Fact]
    public void GetAll_ReturnsNewestFirst_AndGetLatestIsTheNewest()
    {
        _store.Write(Rec("20261028-100000-wind-aaaa", minutes: 0));
        _store.Write(Rec("20261028-120000-wind-bbbb", minutes: 120));
        _store.Write(Rec("20261028-110000-wind-cccc", minutes: 60));

        var (records, _) = _store.GetAll();

        Assert.Equal(["20261028-120000-wind-bbbb", "20261028-110000-wind-cccc", "20261028-100000-wind-aaaa"], records.Select(r => r.Id));
        Assert.Equal("20261028-120000-wind-bbbb", _store.GetLatest().Record!.Id);
    }

    [Fact]
    public void NewExecId_StartsWithTimestamp_SoNameOrderIsTimeOrder()
    {
        var earlier = HistoryRecord.NewExecId("wind", Base);
        var later = HistoryRecord.NewExecId("ion-middleware", Base.AddSeconds(1));

        Assert.Matches(@"^20261028-101530-wind-[0-9a-f]{4}$", earlier);
        Assert.True(string.CompareOrdinal(earlier, later) < 0);
        Assert.Throws<ArgumentException>(() => HistoryRecord.NewExecId("..\\evil", Base));
    }

    [Fact]
    public void GetAll_SkipsBrokenFiles_ReportsThem_AndKeepsTheRest()
    {
        _store.Write(Rec("20261028-100000-wind-aaaa"));
        _store.Write(Rec("20261028-110000-wind-bbbb", minutes: 60));
        string dir = Paths.HistoryDir(_cache);
        File.WriteAllText(Path.Combine(dir, "garbage.ini"), "this is not ini");
        File.WriteAllText(Path.Combine(dir, "bad-outcome.ini"),
            "[Meta]\nSchemaVersion=1.0\n[Execution]\nId=bad-outcome\nStartedAt=2026-10-28T10:15:30+07:00\nOperator=o\nMachine=m\nComponent=wind\nOutcome=Exploded\n");
        File.WriteAllText(Path.Combine(dir, "20261028-130000-wind-zzzz.ini"), "[Meta]\nSchemaVersion=1.0\n[Execution]\nId=other-name\n");
        File.WriteAllText(Path.Combine(dir, "left-over.ini.tmp"), "half written");

        var (records, problems) = _store.GetAll();

        Assert.Equal(2, records.Count);
        Assert.Contains(problems, p => p.Contains("20261028-130000-wind-zzzz.ini"));
        Assert.Contains(problems, p => p.Contains("garbage.ini") && p.Contains("line 1"));
        Assert.Contains(problems, p => p.Contains("bad-outcome.ini") && p.Contains("Exploded"));
        Assert.DoesNotContain(problems, p => p.Contains(".tmp"));
        Assert.Equal("20261028-110000-wind-bbbb", _store.GetLatest().Record!.Id);
    }

    [Fact]
    public void Write_RejectsAnExistingId_AndKeepsTheOriginalFile()
    {
        var first = Rec("20261028-100000-wind-aaaa");
        Assert.Empty(_store.Write(first));
        string path = Paths.HistoryIni(_cache, first.Id);
        string before = File.ReadAllText(path);

        var errors = _store.Write(first with { Outcome = HistoryOutcome.Rejected, Machine = "OTHER" });

        Assert.Contains("already exists", Assert.Single(errors));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Write_RejectsInvalidRecords_WithoutCreatingFiles()
    {
        Assert.NotEmpty(_store.Write(Rec("..\\evil")));
        Assert.NotEmpty(_store.Write(Rec("ok-id") with { Operator = "" }));
        Assert.NotEmpty(_store.Write(Rec("ok-id") with { Operations = ["a,b"] }));
        Assert.NotEmpty(_store.Write(Rec("ok-id") with { Outcome = (HistoryOutcome)99 }));
        Assert.False(Directory.Exists(Paths.HistoryDir(_cache)));
    }

    [Fact]
    public void LongMultiLineErrorMessage_RoundTrips()
    {
        string message = "Copy failed at step 3.\r\nAccess to the path 'D:\\APCONTAINER\\WIND\\new.dll' is denied.\n\n" +
                         "Tab\there ; semicolon, key=value, trailing slash \\\nC:\\new\\ref\\temp\\sub ; end  " + new string('x', 5000) + "  ";
        var saved = Rec("20261028-100000-wind-aaaa", outcome: HistoryOutcome.FailedRestored) with
        {
            ErrorMessage = message, RestoreFailures = ["first ; failure", "second\nfailure"],
        };

        Assert.Empty(_store.Write(saved));
        var loaded = Assert.Single(_store.GetAll().Records);

        Assert.Equal(message, loaded.ErrorMessage);
        Assert.Equal(["first ; failure", "second failure"], loaded.RestoreFailures);   // item satu baris
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(@"D:\ION-PackMan\Cache\Backups\x")]
    [InlineData(@"C:\new\table\sub\run")]
    [InlineData(@"\n \\n \\\n \; \s \u0041 \")]
    [InlineData("  lead and trail  ")]
    [InlineData("a ; b ;c; d")]
    [InlineData("line1\r\nline2\rline3\nline4")]
    [InlineData("\u00A0nbsp edges\u00A0")]
    public void EscapeThenUnescape_IsLossless_AndProducesAnIniSafeValue(string text)
    {
        string escaped = HistoryStore.Escape(text);

        Assert.Equal(text, HistoryStore.Unescape(escaped));
        Assert.DoesNotContain('\n', escaped);
        Assert.DoesNotContain('\r', escaped);
        Assert.DoesNotContain(" ;", escaped);
        Assert.Equal(escaped, escaped.Trim());
    }

    [Fact]
    public void MissingFolder_MeansEmptyList()
    {
        var (records, problems) = _store.GetAll();
        var (latest, latestProblems) = _store.GetLatest();

        Assert.Empty(records);
        Assert.Empty(problems);
        Assert.Null(latest);
        Assert.Empty(latestProblems);
    }

    [Fact]
    public void Store_HasNoWayToChangeOrDeleteHistory()
    {
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
        var methods = typeof(HistoryStore).GetMethods(flags).Select(m => m.Name).Order().ToArray();

        Assert.Equal(["GetAll", "GetLatest", "Write"], methods);
    }
}
