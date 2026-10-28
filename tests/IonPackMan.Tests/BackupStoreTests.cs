using IonPackMan.Deployment;
using IonPackMan.Ini;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>
/// Tes BackupStore (ADR-004). Semua di folder sementara dari TestDeployEnvironment, FakeRegistry, dan FakeServiceControl;
/// tidak ada yang menyentuh Registry, service, atau file sungguhan.
/// </summary>
public sealed class BackupStoreTests : IDisposable
{
    private readonly TestDeployEnvironment _env = new(importBuild: false);
    private readonly BackupStore _store;
    private int _seq;

    public BackupStoreTests() => _store = NewStore();

    public void Dispose() => _env.Dispose();

    private BackupStore NewStore() => new(_env.Cache, _env.Registry, _env.Services, _env.Allow);

    private string NextId() => $"20261029-100000-wind-{++_seq:0000}";

    private string ExeFile => Path.Combine(_env.Target, "WIND.exe");
    private string NewLib => Path.Combine(_env.Target, "lib", "x.dll");
    private string NewDeep => Path.Combine(_env.Target, "deep", "a", "b.txt");

    private static PlannedFile File_(string target, FileChange change) => new(Path.GetFileName(target), "src", target, 0, "", change);

    /// <summary>Plan lengkap: file diganti, dua file baru (di direktori baru), Registry dan INI (ada dan belum ada), service WIND.</summary>
    private DeploymentPlan FullPlan(string? execId = null) => new()
    {
        ExecId = execId ?? NextId(), ProfileId = "TESTP", ComponentId = "wind", ReleaseVersion = "2.1.0",
        TargetDirectory = _env.Target, ServiceName = "WIND",
        DeployFiles = true, ApplyConfig = true, ServiceStateAtPlan = ServiceState.Running,
        Files =
        [
            File_(ExeFile, FileChange.Replace),
            File_(NewLib, FileChange.New),
            File_(NewDeep, FileChange.New),
        ],
        RegistryChanges =
        [
            new("WebUISupport", TestDeployEnvironment.RegKey, "Support", RegistryValueType.DWord, "1", "0"),
            new("Fresh", TestDeployEnvironment.RegKey, "Fresh", RegistryValueType.String, "v", null),
        ],
        IniChanges =
        [
            new("WebUISupport", _env.AppIni, "WebUI", "Support", "1", "0"),
            new("Fresh", _env.AppIni, "WebUI", "Fresh", "x", null),
        ],
    };

    private Backup CreateOk(DeploymentPlan plan)
    {
        var (backup, errors) = _store.Create(plan);
        Assert.True(backup is not null, string.Join("; ", errors));
        Assert.Empty(errors);
        return backup;
    }

    private string BackupIni(string execId) => Path.Combine(Paths.BackupDir(_env.Cache, execId), "backup.ini");

    /// <summary>Meniru Deployer: mengubah SEMUA yang dicatat plan. Tiap bagian bisa dimatikan untuk uji "baru sebagian jalan".</summary>
    private void SimulateDeploy(bool stopService = true, bool files = true, bool registry = true, bool ini = true)
    {
        if (stopService) _env.Services.Stop("WIND", TimeSpan.FromSeconds(1));
        if (files)
        {
            File.WriteAllText(ExeFile, "new-exe");
            Directory.CreateDirectory(Path.GetDirectoryName(NewLib)!);
            File.WriteAllText(NewLib, "dll");
            Directory.CreateDirectory(Path.GetDirectoryName(NewDeep)!);
            File.WriteAllText(NewDeep, "deep");
        }
        if (registry)
        {
            _env.Registry.Write(TestDeployEnvironment.RegKey, "Support", RegistryValueType.DWord, "1");
            _env.Registry.Write(TestDeployEnvironment.RegKey, "Fresh", RegistryValueType.String, "v");
        }
        if (ini)
        {
            AppIniEditor.Write(_env.AppIni, "WebUI", "Support", "1");
            AppIniEditor.Write(_env.AppIni, "WebUI", "Fresh", "x");
        }
    }

    /// <summary>Seluruh keadaan mesin palsu: isi file, direktori, Registry, state service.</summary>
    private string Fingerprint()
    {
        var files = TestDeployEnvironment.SnapshotFolder(_env.Apps).Select(kv => $"F {kv.Key}={kv.Value}");
        var dirs = Directory.EnumerateDirectories(_env.Apps, "*", SearchOption.AllDirectories)
            .Select(d => "D " + Path.GetRelativePath(_env.Apps, d)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var registry = _env.Registry.Snapshot().OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"R {kv.Key}={kv.Value.Type}:{kv.Value.Value}");
        return string.Join("\n", files.Concat(dirs).Concat(registry).Append("S " + _env.Services.GetState("WIND")));
    }

    // ---------- Create ----------

    [Fact]
    public void Create_WritesInProgressBackupIni_BeforeReturningSuccess()
    {
        var plan = FullPlan();
        var backup = CreateOk(plan);

        Assert.True(File.Exists(BackupIni(plan.ExecId)));
        var onDisk = IniFile.Load(BackupIni(plan.ExecId));
        Assert.Equal("InProgress", onDisk.Get("Backup", "Status"));
        Assert.Equal("true", onDisk.Get("Backup", "ServiceWasRunning"));
        Assert.Equal("WIND", onDisk.Get("Backup", "ServiceName"));
        Assert.Equal(BackupStatus.InProgress, backup.Status);
        Assert.Equal([ExeFile], backup.ReplacedFiles);
        Assert.Equal([NewLib, NewDeep], backup.NewFiles);
        // Direktori baru: terdalam dulu.
        Assert.Equal(3, backup.NewDirectories.Count);
        Assert.Equal(Path.Combine(_env.Target, "deep", "a"), backup.NewDirectories[0]);
        Assert.Equal(Path.Combine(_env.Target, "deep"), backup.NewDirectories[1]);
        Assert.Equal(Path.Combine(_env.Target, "lib"), backup.NewDirectories[2]);
        // Salinan file ada di dalam folder backup, isinya sama.
        string copy = Directory.EnumerateFiles(Path.Combine(Paths.BackupDir(_env.Cache, plan.ExecId), "files"), "WIND.exe", SearchOption.AllDirectories).Single();
        Assert.Equal("old-exe", File.ReadAllText(copy));
    }

    [Fact]
    public void Create_ChangesNothingOnTheMachine()
    {
        string before = Fingerprint();
        CreateOk(FullPlan());
        Assert.Equal(before, Fingerprint());
        Assert.DoesNotContain(_env.Services.Calls, c => c.StartsWith("Stop") || c.StartsWith("Start"));
        Assert.DoesNotContain(_env.Registry.Operations, o => o.StartsWith("Write") || o.StartsWith("Delete"));
    }

    [Fact]
    public void Create_RecordsOldValues_ReadFreshInsteadOfTrustingPlan()
    {
        var plan = FullPlan() with
        {
            RegistryChanges = [new("WebUISupport", TestDeployEnvironment.RegKey, "Support", RegistryValueType.DWord, "1", "999")],
        };
        var backup = CreateOk(plan);

        var reg = Assert.Single(backup.RegistryEntries);
        Assert.True(reg.Existed);
        Assert.Equal(RegistryValueType.DWord, reg.Type);
        Assert.Equal("0", reg.OldValue); // nilai nyata, bukan "999" dari plan yang basi
        Assert.Contains(backup.IniEntries, e => e.Name == "WebUISupport" && e.Existed && e.OldValue == "0");
        Assert.Contains(backup.IniEntries, e => e.Name == "Fresh" && !e.Existed);
    }

    [Fact]
    public void Create_DuplicateExecId_IsRejected_AndExistingBackupIsNotOverwritten()
    {
        var plan = FullPlan();
        CreateOk(plan);
        string iniBefore = File.ReadAllText(BackupIni(plan.ExecId));

        var (backup, errors) = _store.Create(plan);

        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("already exists"));
        Assert.Equal(iniBefore, File.ReadAllText(BackupIni(plan.ExecId)));
    }

    [Fact]
    public void Create_CopyDiffersFromOriginal_CancelsAndLeavesNoBackupBehind()
    {
        var plan = FullPlan();
        _store.AfterFileCopied = (target, _) => File.WriteAllText(target, "changed-in-the-middle");

        var (backup, errors) = _store.Create(plan);

        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("does not match"));
        Assert.False(Directory.Exists(Paths.BackupDir(_env.Cache, plan.ExecId)));
        Assert.Empty(_store.FindUnfinished().Unfinished);
    }

    [Theory]
    [InlineData("a ;b")]       // dibaca INI sebagai komentar
    [InlineData("trailing ")]  // spasi di ujung
    public void Create_RegistryValueIniCannotStore_FailsClearlyBeforeAnyChange(string oldValue)
    {
        _env.Registry.Seed(TestDeployEnvironment.RegKey, "Support", RegistryValueType.String, oldValue);
        var plan = FullPlan();
        string before = Fingerprint();

        var (backup, errors) = _store.Create(plan);

        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("cannot be recorded in backup.ini"));
        Assert.Equal(before, Fingerprint());
        Assert.False(Directory.Exists(Paths.BackupDir(_env.Cache, plan.ExecId)));
    }

    [Fact]
    public void Create_RegistryValueWithNewline_FailsClearly()
    {
        _env.Registry.Seed(TestDeployEnvironment.RegKey, "Support", RegistryValueType.String, "line1\nline2");
        var (backup, errors) = _store.Create(FullPlan());
        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("cannot be recorded in backup.ini"));
    }

    [Fact]
    public void Create_FileOutsideAllowList_Rejected()
    {
        string outside = Path.Combine(_env.Root, "outside", "x.txt");
        var plan = FullPlan() with { Files = [File_(outside, FileChange.New)] };
        var (backup, errors) = _store.Create(plan);
        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("outside the allowed base directories"));
    }

    [Fact]
    public void Create_NewFileThatAlreadyExists_Rejected_SoRestoreNeverDeletesSomeoneElsesFile()
    {
        var plan = FullPlan() with { Files = [File_(Path.Combine(_env.Target, "keep.txt"), FileChange.New)] };
        var (backup, errors) = _store.Create(plan);
        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("already exists"));
    }

    [Fact]
    public void Create_ReplaceFileThatNoLongerExists_Rejected()
    {
        var plan = FullPlan() with { Files = [File_(Path.Combine(_env.Target, "gone.dll"), FileChange.Replace)] };
        var (backup, errors) = _store.Create(plan);
        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("no longer exists"));
    }

    [Fact]
    public void Create_RegistryKeyOrIniFileOrServiceOutsideAllowList_Rejected()
    {
        var plan = FullPlan() with
        {
            ServiceName = "OTHER",
            RegistryChanges = [new("R", @"HKLM\SOFTWARE\Other", "V", RegistryValueType.String, "1", null)],
            IniChanges = [new("I", Path.Combine(_env.Root, "other.ini"), "S", "K", "1", null)],
        };
        var (backup, errors) = _store.Create(plan);
        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("Service 'OTHER'"));
        Assert.Contains(errors, e => e.Contains("Registry key"));
        Assert.Contains(errors, e => e.Contains("INI file"));
    }

    [Fact]
    public void Create_DuplicateChangeNames_Rejected()
    {
        var plan = FullPlan() with
        {
            RegistryChanges =
            [
                new("Same", TestDeployEnvironment.RegKey, "A", RegistryValueType.String, "1", null),
                new("same", TestDeployEnvironment.RegKey, "B", RegistryValueType.String, "1", null),
            ],
        };
        var (backup, errors) = _store.Create(plan);
        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("used more than once"));
    }

    [Fact]
    public void Create_UnsafeExecId_Rejected()
    {
        foreach (string id in new[] { "", "..", @"..\evil", "a/b", ".hidden" })
        {
            var (backup, errors) = _store.Create(FullPlan(id));
            Assert.Null(backup);
            Assert.NotEmpty(errors);
        }
    }

    // ---------- Restore: isi identik ----------

    [Fact]
    public void CreateChangeRestore_EverythingIdenticalByteForByte()
    {
        string before = Fingerprint();
        byte[] exeBytes = File.ReadAllBytes(ExeFile);
        byte[] iniBytes = File.ReadAllBytes(_env.AppIni);
        var plan = FullPlan();
        var backup = CreateOk(plan);

        SimulateDeploy();
        Assert.NotEqual(before, Fingerprint());
        Assert.Equal(ServiceState.Stopped, _env.Services.GetState("WIND"));

        var failures = _store.Restore(backup);

        Assert.Empty(failures);
        Assert.Equal(before, Fingerprint());
        Assert.Equal(exeBytes, File.ReadAllBytes(ExeFile));
        Assert.Equal(iniBytes, File.ReadAllBytes(_env.AppIni));
        Assert.Equal(ServiceState.Running, _env.Services.GetState("WIND"));
        Assert.Equal("Restored", IniFile.Load(BackupIni(plan.ExecId)).Get("Backup", "Status"));
        Assert.False(Directory.Exists(Path.Combine(_env.Target, "lib")));
        Assert.False(Directory.Exists(Path.Combine(_env.Target, "deep")));
    }

    [Fact]
    public void Restore_Twice_StillIdentical()
    {
        string before = Fingerprint();
        var backup = CreateOk(FullPlan());
        SimulateDeploy();

        Assert.Empty(_store.Restore(backup));
        string afterFirst = Fingerprint();
        Assert.Empty(_store.Restore(backup));

        Assert.Equal(before, afterFirst);
        Assert.Equal(before, Fingerprint());
    }

    [Fact]
    public void Restore_WhenNothingWasChangedYet_IsSafe_AndWritesNothing()
    {
        string before = Fingerprint();
        byte[] iniBytes = File.ReadAllBytes(_env.AppIni);
        var backup = CreateOk(FullPlan());
        _env.Registry.Operations.Clear();

        Assert.Empty(_store.Restore(backup));

        Assert.Equal(before, Fingerprint());
        Assert.Equal(iniBytes, File.ReadAllBytes(_env.AppIni));
        Assert.DoesNotContain(_env.Registry.Operations, o => o.StartsWith("Write") || o.StartsWith("Delete"));
    }

    [Theory]
    [InlineData(true, false, false, false)]   // baru service di-stop
    [InlineData(true, true, false, false)]    // stop + file
    [InlineData(true, true, true, false)]     // stop + file + Registry (INI belum)
    [InlineData(false, false, true, false)]   // hanya Registry
    [InlineData(false, false, false, true)]   // hanya INI
    public void Restore_WhenOnlySomeStepsRan_RestoresEverything(bool stop, bool files, bool registry, bool ini)
    {
        string before = Fingerprint();
        var backup = CreateOk(FullPlan());
        SimulateDeploy(stop, files, registry, ini);

        Assert.Empty(_store.Restore(backup));

        Assert.Equal(before, Fingerprint());
    }

    [Fact]
    public void Restore_ExistedFalse_DeletesRegistryValueAndIniKey()
    {
        var backup = CreateOk(FullPlan());
        SimulateDeploy();
        Assert.NotNull(_env.Registry.Read(TestDeployEnvironment.RegKey, "Fresh"));
        Assert.True(AppIniEditor.Read(_env.AppIni, "WebUI", "Fresh").Exists);

        Assert.Empty(_store.Restore(backup));

        Assert.Null(_env.Registry.Read(TestDeployEnvironment.RegKey, "Fresh"));
        Assert.False(AppIniEditor.Read(_env.AppIni, "WebUI", "Fresh").Exists);
        // nilai lama yang dulu ada dikembalikan, key lain tidak disentuh
        Assert.Equal(new RegistryValue(RegistryValueType.DWord, "0"), _env.Registry.Read(TestDeployEnvironment.RegKey, "Support"));
        Assert.Equal((true, "0"), AppIniEditor.Read(_env.AppIni, "WebUI", "Support"));
        Assert.Equal((true, "keep"), AppIniEditor.Read(_env.AppIni, "WebUI", "Other"));
    }

    [Fact]
    public void Restore_DeletesNewFiles_ButKeepsDirectoryThatHoldsOtherFiles()
    {
        var backup = CreateOk(FullPlan());
        SimulateDeploy();
        string stranger = Path.Combine(_env.Target, "lib", "someone-elses.txt");
        File.WriteAllText(stranger, "mine");

        var failures = _store.Restore(backup);

        Assert.Empty(failures); // folder yang tidak kosong dibiarkan; itu bukan kegagalan
        Assert.False(File.Exists(NewLib));
        Assert.True(File.Exists(stranger));
        Assert.Equal("mine", File.ReadAllText(stranger));
        Assert.True(Directory.Exists(Path.Combine(_env.Target, "lib")));
        Assert.False(Directory.Exists(Path.Combine(_env.Target, "deep"))); // yang kosong dihapus, terdalam dulu
    }

    // ---------- Restore: kegagalan ----------

    [Fact]
    public void Restore_FailuresInTheMiddle_OtherStepsStillRun_ThenRetryEndsRestored()
    {
        string before = Fingerprint();
        var plan = FullPlan();
        var backup = CreateOk(plan);
        SimulateDeploy();
        _env.Registry.FailOn(TestDeployEnvironment.RegKey, "Support");
        _env.Registry.FailOn(TestDeployEnvironment.RegKey, "Fresh");

        var failures = _store.Restore(backup);

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, f => f.Contains("'Support'") || f.Contains("value 'Support'"));
        Assert.Contains(failures, f => f.Contains("Fresh"));
        Assert.Equal("RestoreIncomplete", IniFile.Load(BackupIni(plan.ExecId)).Get("Backup", "Status"));
        // langkah lain tetap berjalan
        Assert.Equal("old-exe", File.ReadAllText(ExeFile));
        Assert.False(File.Exists(NewLib));
        Assert.Equal((true, "0"), AppIniEditor.Read(_env.AppIni, "WebUI", "Support"));
        Assert.False(AppIniEditor.Read(_env.AppIni, "WebUI", "Fresh").Exists);
        Assert.Equal(ServiceState.Running, _env.Services.GetState("WIND"));

        _env.Registry.ClearFailures();
        Assert.Empty(_store.Restore(plan.ExecId));

        Assert.Equal("Restored", IniFile.Load(BackupIni(plan.ExecId)).Get("Backup", "Status"));
        Assert.Equal(before, Fingerprint());
    }

    [Fact]
    public void Restore_ServiceWillNotStart_ReportedAndEverythingElseRestored()
    {
        var plan = FullPlan();
        var backup = CreateOk(plan);
        SimulateDeploy();
        _env.Services.FailOn("Start", "WIND", new TimeoutException("start timeout"));

        var failures = _store.Restore(backup);

        var failure = Assert.Single(failures);
        Assert.Contains("start timeout", failure);
        Assert.Equal("RestoreIncomplete", IniFile.Load(BackupIni(plan.ExecId)).Get("Backup", "Status"));
        Assert.Equal("old-exe", File.ReadAllText(ExeFile));
        Assert.Equal(new RegistryValue(RegistryValueType.DWord, "0"), _env.Registry.Read(TestDeployEnvironment.RegKey, "Support"));
    }

    [Fact]
    public void Restore_ServiceNeverStops_ReportedAndOtherStepsContinue()
    {
        var plan = FullPlan();
        var backup = CreateOk(plan);
        // service masih Running saat Restore (mis. start otomatis sebelum gagal) lalu menolak berhenti
        File.WriteAllText(ExeFile, "new-exe");
        _env.Registry.Write(TestDeployEnvironment.RegKey, "Support", RegistryValueType.DWord, "1");
        _env.Services.NeverStops("WIND");

        var failures = _store.Restore(backup);

        Assert.Contains(failures, f => f.Contains("stop service"));
        Assert.Equal(new RegistryValue(RegistryValueType.DWord, "0"), _env.Registry.Read(TestDeployEnvironment.RegKey, "Support"));
        Assert.Equal("old-exe", File.ReadAllText(ExeFile)); // salinan tetap dicoba
    }

    [Fact]
    public void Restore_BackupCopyMissing_ReportedAsFailure()
    {
        var plan = FullPlan();
        var backup = CreateOk(plan);
        SimulateDeploy();
        Directory.Delete(Path.Combine(Paths.BackupDir(_env.Cache, plan.ExecId), "files"), recursive: true);

        var failures = _store.Restore(backup);

        Assert.Contains(failures, f => f.Contains("missing"));
        Assert.Equal("RestoreIncomplete", IniFile.Load(BackupIni(plan.ExecId)).Get("Backup", "Status"));
        Assert.False(File.Exists(NewLib)); // langkah lain tetap jalan
    }

    // ---------- Service ----------

    [Fact]
    public void Service_WasRunning_IsRunningAgainAfterRestore()
    {
        var backup = CreateOk(FullPlan());
        SimulateDeploy();
        Assert.Equal(ServiceState.Stopped, _env.Services.GetState("WIND"));

        Assert.Empty(_store.Restore(backup));

        Assert.Equal(ServiceState.Running, _env.Services.GetState("WIND"));
    }

    [Fact]
    public void Service_WasStopped_StaysStopped_AndIsStoppedIfDeployStartedIt()
    {
        using var env = new TestDeployEnvironment(wind: ServiceState.Stopped, importBuild: false);
        var store = new BackupStore(env.Cache, env.Registry, env.Services, env.Allow);
        var plan = new DeploymentPlan
        {
            ExecId = NextId(), ProfileId = "TESTP", ComponentId = "wind", ReleaseVersion = "2.1.0",
            TargetDirectory = env.Target, ServiceName = "WIND", RestartService = true,
            Files = [File_(Path.Combine(env.Target, "WIND.exe"), FileChange.Replace)],
        };
        var (backup, errors) = store.Create(plan);
        Assert.True(backup is not null, string.Join("; ", errors));
        Assert.False(backup.ServiceWasRunning);

        // Restore saat service tetap Stopped: dibiarkan
        Assert.Empty(store.Restore(backup));
        Assert.Equal(ServiceState.Stopped, env.Services.GetState("WIND"));
        Assert.DoesNotContain(env.Services.Calls, c => c == "Start:WIND");

        // Deploy sempat menyalakan service: Restore menghentikannya
        env.Services.StartAndWaitRunning("WIND", TimeSpan.FromSeconds(1));
        Assert.Empty(store.Restore(backup));
        Assert.Equal(ServiceState.Stopped, env.Services.GetState("WIND"));
    }

    // ---------- Crash dan FindUnfinished ----------

    [Fact]
    public void AfterCrash_NewStoreFindsUnfinishedBackup_AndRestoresFromBackupIni()
    {
        string before = Fingerprint();
        var plan = FullPlan();
        CreateOk(plan);
        SimulateDeploy(); // lalu "aplikasi mati": objek lama dibuang

        var fresh = NewStore();
        var (unfinished, problems) = fresh.FindUnfinished();

        Assert.Empty(problems);
        var found = Assert.Single(unfinished);
        Assert.Equal(plan.ExecId, found.ExecId);
        Assert.Equal(BackupStatus.InProgress, found.Status);

        Assert.Empty(fresh.Restore(found));
        Assert.Equal(before, Fingerprint());
        Assert.Empty(fresh.FindUnfinished().Unfinished);
    }

    [Fact]
    public void FindUnfinished_IgnoresDoneAndRestored_ListsInProgress()
    {
        var done = CreateOk(FullPlan());
        _store.MarkDone(done);
        var restored = CreateOk(FullPlan());
        Assert.Empty(_store.Restore(restored));
        var pending = CreateOk(FullPlan());

        var (unfinished, problems) = _store.FindUnfinished();

        Assert.Empty(problems);
        Assert.Equal(pending.ExecId, Assert.Single(unfinished).ExecId);
    }

    [Fact]
    public void MarkDone_ThenFindUnfinished_NoLongerListsIt()
    {
        var plan = FullPlan();
        var backup = CreateOk(plan);
        Assert.Single(_store.FindUnfinished().Unfinished);

        _store.MarkDone(backup);

        Assert.Empty(_store.FindUnfinished().Unfinished);
        Assert.Equal("Done", IniFile.Load(BackupIni(plan.ExecId)).Get("Backup", "Status"));
        Assert.Equal(BackupStatus.Done, _store.Load(plan.ExecId).Backup!.Status);
    }

    [Fact]
    public void MarkDone_OnlyFromInProgress()
    {
        var backup = CreateOk(FullPlan());
        Assert.Empty(_store.Restore(backup)); // Restored
        Assert.Throws<InvalidOperationException>(() => _store.MarkDone(backup));
    }

    [Fact]
    public void Restore_OfDoneBackup_IsRefused_AndTouchesNothing()
    {
        var backup = CreateOk(FullPlan());
        SimulateDeploy();
        _store.MarkDone(backup);
        string afterDeploy = Fingerprint();

        var failures = _store.Restore(backup);

        Assert.Contains(failures, f => f.Contains("Status=Done"));
        Assert.Equal(afterDeploy, Fingerprint());
    }

    [Fact]
    public void FindUnfinished_ListsRestoreIncompleteBackups()
    {
        var plan = FullPlan();
        var backup = CreateOk(plan);
        SimulateDeploy();
        _env.Registry.FailOn(TestDeployEnvironment.RegKey, "Support");
        Assert.NotEmpty(_store.Restore(backup));

        var found = Assert.Single(NewStore().FindUnfinished().Unfinished);
        Assert.Equal(BackupStatus.RestoreIncomplete, found.Status);

        _env.Registry.ClearFailures();
        Assert.Empty(_store.Restore(backup));
        Assert.Empty(NewStore().FindUnfinished().Unfinished);
    }

    [Fact]
    public void FindUnfinished_CorruptBackupIni_IsReportedAsProblem_NotIgnored()
    {
        var good = FullPlan();
        CreateOk(good);
        var bad = FullPlan();
        CreateOk(bad);
        File.WriteAllText(BackupIni(bad.ExecId), "[Backup\r\nthis is not ini");

        var (unfinished, problems) = _store.FindUnfinished();

        Assert.Equal(good.ExecId, Assert.Single(unfinished).ExecId);
        var problem = Assert.Single(problems);
        Assert.Contains(bad.ExecId, problem);
        Assert.Contains("unreadable", problem);
    }

    [Fact]
    public void FindUnfinished_FolderWithoutBackupIni_IsReportedAsProblem()
    {
        string folder = Paths.BackupDir(_env.Cache, "20261029-leftover");
        Directory.CreateDirectory(Path.Combine(folder, "files"));

        var (unfinished, problems) = _store.FindUnfinished();

        Assert.Empty(unfinished);
        Assert.Contains(problems, p => p.Contains("20261029-leftover") && p.Contains("no backup.ini"));
    }

    [Fact]
    public void Restore_CorruptBackupIni_TouchesNothing_AndReports()
    {
        var plan = FullPlan();
        CreateOk(plan);
        SimulateDeploy();
        string afterDeploy = Fingerprint();
        File.WriteAllText(BackupIni(plan.ExecId), "garbage without sections");

        var failures = _store.Restore(plan.ExecId);

        Assert.NotEmpty(failures);
        Assert.Equal(afterDeploy, Fingerprint());
    }

    [Fact]
    public void Load_StatusThatIsNotAKnownName_IsInvalid()
    {
        var plan = FullPlan();
        CreateOk(plan);
        Edit(plan.ExecId, ini => ini.Set("Backup", "Status", "7"));

        var (backup, errors) = _store.Load(plan.ExecId);

        Assert.Null(backup);
        Assert.Contains(errors, e => e.Contains("Status '7'"));
    }

    // ---------- Keamanan: backup.ini dimanipulasi ----------

    private void Edit(string execId, Action<IniFile> change)
    {
        var ini = IniFile.Load(BackupIni(execId));
        change(ini);
        ini.Save(BackupIni(execId));
    }

    [Fact]
    public void Restore_TamperedEntriesOutsideAllowList_AreNotTouched_AndReported()
    {
        // Korban di luar allow-list: file, key Registry, INI aplikasi, service.
        string outsideDir = Path.Combine(_env.Root, "outside");
        Directory.CreateDirectory(outsideDir);
        string victim = Path.Combine(outsideDir, "victim.txt");
        string victim2 = Path.Combine(outsideDir, "victim2.txt");
        string victimIni = Path.Combine(outsideDir, "other.ini");
        File.WriteAllText(victim, "precious");
        File.WriteAllText(victim2, "precious2");
        File.WriteAllText(victimIni, "[S]\r\nK=keep\r\n");
        const string otherKey = @"HKLM\SOFTWARE\Other";
        _env.Registry.Seed(otherKey, "V", RegistryValueType.String, "keep");
        _env.Services.Add("OTHER", ServiceState.Running);

        var plan = FullPlan();
        CreateOk(plan);
        string dotDot = Path.Combine(_env.Target, "..", "..", "outside", "victim2.txt");
        Edit(plan.ExecId, ini =>
        {
            ini.Set("NewFiles", "10", victim);                         // akan dihapus bila tidak dicek
            ini.Set("ReplacedFiles", "10", dotDot);                    // lolos secara leksikal, keluar lewat ".."
            ini.Set("NewFiles", "11", "relative\\path.txt");           // path relatif
            ini.Set("NewFiles", "12", @"\\server\share\x.txt");        // UNC
            ini.Set("NewDirectories", "10", outsideDir);               // folder di luar
            ini.Set("Registry:Evil", "Key", otherKey);
            ini.Set("Registry:Evil", "ValueName", "V");
            ini.Set("Registry:Evil", "Existed", "false");
            ini.Set("Registry:Evil", "OldValue", "");
            ini.Set("Ini:Evil", "File", victimIni);
            ini.Set("Ini:Evil", "Section", "S");
            ini.Set("Ini:Evil", "Key", "K");
            ini.Set("Ini:Evil", "Existed", "false");
            ini.Set("Ini:Evil", "OldValue", "");
            ini.Set("Backup", "ServiceName", "OTHER");
        });
        _env.Services.Calls.Clear();

        var failures = _store.Restore(plan.ExecId);

        Assert.Equal("precious", File.ReadAllText(victim));
        Assert.Equal("precious2", File.ReadAllText(victim2));
        Assert.True(Directory.Exists(outsideDir));
        Assert.Equal(new RegistryValue(RegistryValueType.String, "keep"), _env.Registry.Read(otherKey, "V"));
        Assert.Equal((true, "keep"), AppIniEditor.Read(victimIni, "S", "K"));
        Assert.DoesNotContain(_env.Services.Calls, c => c.Contains("OTHER") && !c.StartsWith("GetState"));
        Assert.Equal(ServiceState.Running, _env.Services.GetState("OTHER"));

        Assert.Contains(failures, f => f.Contains(victim) && f.Contains("Skipped"));
        Assert.Contains(failures, f => f.Contains("victim2.txt") && f.Contains("Skipped"));
        Assert.Contains(failures, f => f.Contains("relative") && f.Contains("Skipped"));
        Assert.Contains(failures, f => f.Contains("server") && f.Contains("Skipped"));
        Assert.Contains(failures, f => f.Contains(otherKey));
        Assert.Contains(failures, f => f.Contains("other.ini"));
        Assert.Contains(failures, f => f.Contains("'OTHER'"));
        Assert.Equal("RestoreIncomplete", IniFile.Load(BackupIni(plan.ExecId)).Get("Backup", "Status"));
    }

    [Fact]
    public void Restore_TamperedEntries_DoNotStopLegitimateEntriesFromBeingRestored()
    {
        string before = Fingerprint();
        var plan = FullPlan();
        CreateOk(plan);
        SimulateDeploy();
        Edit(plan.ExecId, ini => ini.Set("NewFiles", "10", Path.Combine(_env.Root, "outside", "x.txt")));

        var failures = _store.Restore(plan.ExecId);

        Assert.Single(failures);
        Assert.Equal(before, Fingerprint());
    }

    [Fact]
    public void Restore_IsNeverAbleToReadACopyFromOutsideTheBackupFolder()
    {
        // Salinan selalu diturunkan dari path target yang lolos allow-list, bukan dari teks di backup.ini.
        var plan = FullPlan();
        CreateOk(plan);
        string secret = Path.Combine(_env.Root, "secret.txt");
        File.WriteAllText(secret, "secret");
        Edit(plan.ExecId, ini => ini.Set("ReplacedFiles", "10", Path.Combine(_env.Target, "..", "..", "secret.txt")));

        var failures = _store.Restore(plan.ExecId);

        Assert.Contains(failures, f => f.Contains("secret.txt") && f.Contains("Skipped"));
        Assert.Equal("secret", File.ReadAllText(secret));
    }
}
