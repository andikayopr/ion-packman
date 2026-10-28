using IonPackMan.Deployment;
using IonPackMan.History;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>
/// Uji kegagalan di SETIAP langkah (tiket T26). Semua pada mesin palsu di folder sementara dengan Registry dan service palsu.
/// Inti yang dibuktikan: gagal di mana pun → isi mesin palsu identik dengan sebelum eksekusi.
/// </summary>
public class DeployerTests
{
    private sealed record MachineState(
        SortedDictionary<string, string> Files, Dictionary<string, RegistryValue> Registry, ServiceState Wind);

    private static MachineState StateOf(TestDeployEnvironment env) => new(
        TestDeployEnvironment.SnapshotFolder(env.Apps),
        env.Registry.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value),
        env.Services.GetState("WIND"));

    private static void AssertSame(MachineState expected, MachineState actual)
    {
        Assert.Equal(expected.Files, actual.Files);
        Assert.Equal(expected.Registry, actual.Registry);
        Assert.Equal(expected.Wind, actual.Wind);
    }

    private sealed class Rig
    {
        public required TestDeployEnvironment Env;
        public required BackupStore Backups;
        public required HistoryStore History;
        public required Deployer Deployer;
        public required DeploymentPlan Plan;
    }

    private static Rig Prepare(TestDeployEnvironment env, PlanRequest? request = null, Action<BackupStore>? tweak = null)
    {
        var (plan, errors) = env.Planner.Build(request ?? TestDeployEnvironment.All());
        Assert.True(plan is not null, string.Join("; ", errors));

        var backups = new BackupStore(env.Cache, env.Registry, env.Services, env.Allow);
        tweak?.Invoke(backups);
        var history = new HistoryStore(env.Cache);
        var deployer = new Deployer(env.Machine, env.Guards, backups, env.Services, env.Registry, history, TimeSpan.FromSeconds(1));
        return new Rig { Env = env, Backups = backups, History = history, Deployer = deployer, Plan = plan };
    }

    // ---------- Sukses ----------

    [Fact]
    public void FullDeployment_Succeeds_AndAppliesEverything()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env);
        var steps = new List<DeployStep>();

        var result = rig.Deployer.Run(rig.Plan, new SyncProgress(steps.Add));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Empty(result.HistoryProblems);
        Assert.Equal("new-exe", File.ReadAllText(Path.Combine(env.Target, "WIND.exe")));
        Assert.Equal("cfg", File.ReadAllText(Path.Combine(env.Target, "config.txt")));
        Assert.Equal("dll", File.ReadAllText(Path.Combine(env.Target, "lib", "x.dll")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(env.Target, "keep.txt")));   // file yang tidak ada di build tidak disentuh

        Assert.Equal(new RegistryValue(RegistryValueType.DWord, "1"), env.Registry.Read(TestDeployEnvironment.RegKey, "Support"));
        Assert.Equal((true, "1"), AppIniEditor.Read(env.AppIni, "WebUI", "Support"));
        Assert.Equal((true, "keep"), AppIniEditor.Read(env.AppIni, "WebUI", "Other"));   // sisa INI aplikasi tidak berubah

        Assert.Equal(new[] { "Stop:WIND", "Start:WIND" }, env.Services.Calls.Where(c => c != "GetState:WIND"));
        Assert.Equal(ServiceState.Running, env.Services.GetState("WIND"));
        Assert.Equal(new[] { DeployStep.Checking, DeployStep.BackingUp, DeployStep.StoppingService, DeployStep.CopyingFiles,
                             DeployStep.ApplyingConfig, DeployStep.StartingService, DeployStep.Finalizing }, steps);

        var (unfinished, problems) = rig.Backups.FindUnfinished();
        Assert.Empty(unfinished);
        Assert.Empty(problems);
        Assert.Equal(BackupStatus.Done, rig.Backups.Load(rig.Plan.ExecId).Backup!.Status);

        var record = Assert.Single(rig.History.GetAll().Records);
        Assert.Equal((HistoryOutcome.Succeeded, "wind", "2.1.0", "TESTP", "TEST-PC"), (record.Outcome, record.Component, record.BuildVersion, record.Profile, record.Machine));
        Assert.Equal(new[] { "DeployFiles", "ApplyConfig" }, record.Operations);
        Assert.Equal(result.BackupDirectory, record.BackupDirectory);
    }

    private sealed class SyncProgress(Action<DeployStep> onReport) : IProgress<DeployStep>
    {
        public void Report(DeployStep value) => onReport(value);   // Progress<T> bawaan memakai thread pool; ini sinkron agar urutan pasti
    }

    [Fact]
    public void RestartOnly_StopsAndStartsTheService_TouchingNoFiles()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env, new("TESTP", "wind", "2.1.0", false, false, true));
        var files = TestDeployEnvironment.SnapshotFolder(env.Apps);

        var result = rig.Deployer.Run(rig.Plan);

        Assert.True(result.Succeeded);
        Assert.Equal(files, TestDeployEnvironment.SnapshotFolder(env.Apps));
        Assert.Equal(new[] { "Stop:WIND", "Start:WIND" }, env.Services.Calls.Where(c => c != "GetState:WIND"));
    }

    [Fact]
    public void ApplyConfigOnly_DoesNotTouchTheService()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env, new("TESTP", "wind", "2.1.0", false, true, false));

        var result = rig.Deployer.Run(rig.Plan);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(env.Services.Calls, c => c.StartsWith("Stop") || c.StartsWith("Start"));
        Assert.Equal("1", env.Registry.Read(TestDeployEnvironment.RegKey, "Support")!.Value);
    }

    // ---------- Penolakan: tidak ada yang berubah ----------

    [Fact]
    public void GuardsAreCheckedAgain_WhenTheBuildChangesAfterPreview()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env);
        var before = StateOf(env);
        File.WriteAllText(Path.Combine(rig.Plan.BuildFilesDirectory, "config.txt"), "changed-after-preview");

        var result = rig.Deployer.Run(rig.Plan);

        Assert.Equal(HistoryOutcome.Rejected, result.Outcome);
        Assert.Contains("hash differs", string.Join(" ", result.Errors));
        AssertSame(before, StateOf(env));
        Assert.DoesNotContain(env.Services.Calls, c => c.StartsWith("Stop"));
        Assert.Equal(HistoryOutcome.Rejected, Assert.Single(rig.History.GetAll().Records).Outcome);   // penolakan juga dicatat
        Assert.False(Directory.Exists(Paths.BackupDir(env.Cache, rig.Plan.ExecId)));                 // tidak ada backup yang dibuat
    }

    [Fact]
    public void MachineThatBecameNonDevelopment_IsRejectedAtExecuteTime()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env);
        var before = StateOf(env);
        var prod = new Guards(env.MachineWith("Production", env.Cache), env.Allow, env.Builds, () => [], _ => long.MaxValue / 2);
        var deployer = new Deployer(env.MachineWith("Production", env.Cache), prod, rig.Backups, env.Services, env.Registry, rig.History);

        var result = deployer.Run(rig.Plan);

        Assert.Equal(HistoryOutcome.Rejected, result.Outcome);
        AssertSame(before, StateOf(env));
    }

    [Fact]
    public void BackupVerificationFailure_RejectsBeforeAnyChange()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env, tweak: b => b.AfterFileCopied = (_, target) => File.AppendAllText(target, "changed-during-backup"));
        var before = StateOf(env);

        var result = rig.Deployer.Run(rig.Plan);

        Assert.Equal(HistoryOutcome.Rejected, result.Outcome);
        AssertSame(before, StateOf(env) with { Files = before.Files }); // WIND.exe diubah oleh hook uji; yang dicek: tidak ada perubahan lain
        Assert.DoesNotContain(env.Services.Calls, c => c.StartsWith("Stop"));
        Assert.Empty(env.Registry.Operations.Where(o => o.StartsWith("Write")));
        Assert.False(File.Exists(Path.Combine(env.Target, "config.txt")));
        Assert.False(Directory.Exists(Paths.BackupDir(env.Cache, rig.Plan.ExecId)));
    }

    [Fact]
    public void RunningThePlanTwice_IsRefused_BecauseTheExecIdIsAlreadyUsed()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env);
        Assert.True(rig.Deployer.Run(rig.Plan).Succeeded);

        var again = rig.Deployer.Run(rig.Plan);

        Assert.NotEqual(HistoryOutcome.Succeeded, again.Outcome);
    }

    // ---------- Gagal di tiap langkah → restore otomatis ----------

    [Fact]
    public void StopTimesOut_NothingChanged_ServiceStillRunning()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env);
        var before = StateOf(env);
        env.Services.NeverStops("WIND");

        var result = rig.Deployer.Run(rig.Plan);

        Assert.Equal(HistoryOutcome.FailedRestored, result.Outcome);
        AssertSame(before, StateOf(env));
        // Restore tidak mencoba menghentikan service lagi: belum ada file yang berubah, jadi tidak perlu.
        Assert.Single(env.Services.Calls, c => c == "Stop:WIND");
        Assert.Equal(HistoryOutcome.FailedRestored, Assert.Single(rig.History.GetAll().Records).Outcome);
        Assert.Equal(BackupStatus.Restored, rig.Backups.Load(rig.Plan.ExecId).Backup!.Status);
    }

    [Fact]
    public void CopyFailsHalfway_EverythingIsRestored()
    {
        using var env = new TestDeployEnvironment();
        // "lib" adalah FILE, sehingga lib\x.dll tidak bisa dibuat. Urutan copy: WIND.exe, config.txt, lalu lib\x.dll.
        // Setelah dua file pertama berubah, langkah ketiga gagal.
        File.WriteAllText(Path.Combine(env.Target, "lib"), "i am a file");
        var rig = Prepare(env);
        var before = StateOf(env);

        var result = rig.Deployer.Run(rig.Plan);

        Assert.Equal(HistoryOutcome.FailedRestored, result.Outcome);
        Assert.NotEmpty(result.Errors);
        AssertSame(before, StateOf(env));
        Assert.Equal("old-exe", File.ReadAllText(Path.Combine(env.Target, "WIND.exe")));
        Assert.False(File.Exists(Path.Combine(env.Target, "config.txt")));
    }

    [Fact]
    public void RegistryWriteRejected_FilesAndServiceAreRestored()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env);
        var before = StateOf(env);
        env.Registry.FailOn(TestDeployEnvironment.RegKey, "Support");
        // Penulisan ditolak sehingga nilainya tidak pernah berubah; Restore hanya menulis bila nilai berbeda,
        // jadi restore tidak ikut tersandung penolakan yang sama.

        var result = rig.Deployer.Run(rig.Plan);

        Assert.Equal(HistoryOutcome.FailedRestored, result.Outcome);
        AssertSame(before, StateOf(env));
    }

    [Fact]
    public void IniWriteFails_RegistryAndFilesAreRestored()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env);
        var before = StateOf(env);
        var iniBefore = File.ReadAllText(env.AppIni);
        File.SetAttributes(env.AppIni, FileAttributes.ReadOnly);   // WritePrivateProfileString gagal pada file read-only
        try
        {
            var result = rig.Deployer.Run(rig.Plan);

            Assert.Equal(HistoryOutcome.FailedRestored, result.Outcome);
            AssertSame(before, StateOf(env));
            Assert.Equal(iniBefore, File.ReadAllText(env.AppIni));
        }
        finally
        {
            File.SetAttributes(env.AppIni, FileAttributes.Normal);
        }
    }

    [Fact]
    public void ServiceDoesNotStart_ReportsRestoreIncomplete_ButEverythingElseIsRestored()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env);
        var before = StateOf(env);
        env.Services.FailOn("Start", "WIND", new TimeoutException("service did not reach Running"));

        var result = rig.Deployer.Run(rig.Plan);

        // Service tidak bisa dinyalakan, juga saat restore: itu harus dilaporkan, bukan disembunyikan.
        Assert.Equal(HistoryOutcome.FailedRestoreIncomplete, result.Outcome);
        Assert.Contains("service", string.Join(" ", result.RestoreFailures), StringComparison.OrdinalIgnoreCase);
        var after = StateOf(env);
        Assert.Equal(before.Files, after.Files);
        Assert.Equal(before.Registry, after.Registry);

        // Backup tetap tertunda → Developer Console terkunci sampai Restore berhasil.
        var (unfinished, _) = rig.Backups.FindUnfinished();
        Assert.Equal(rig.Plan.ExecId, Assert.Single(unfinished).ExecId);
        Assert.Equal(BackupStatus.RestoreIncomplete, unfinished[0].Status);

        // Rangkaian nyata: Guards bertanya ke BackupStore, jadi eksekusi berikutnya diblokir sampai Restore berhasil.
        var guards = new Guards(env.Machine, env.Allow, env.Builds,
            () => rig.Backups.FindUnfinished().Unfinished.Select(b => $"{b.ExecId} ({b.Status})").ToList(), _ => long.MaxValue / 2);
        Assert.Contains("unfinished backup must be restored first", string.Join(" ", guards.Check(rig.Plan with { ExecId = "next" })));
    }

    [Fact]
    public void RestoreIncomplete_CanBeRestoredAgainAfterTheCauseIsFixed()
    {
        using var env = new TestDeployEnvironment();
        var rig = Prepare(env);
        var before = StateOf(env);
        env.Services.FailOn("Start", "WIND", new TimeoutException("stuck"));
        Assert.Equal(HistoryOutcome.FailedRestoreIncomplete, rig.Deployer.Run(rig.Plan).Outcome);

        // Penyebab diperbaiki: service bisa dinyalakan lagi.
        var fixedServices = new FakeServiceControl().Add("WIND", ServiceState.Stopped);
        var recovery = new BackupStore(env.Cache, env.Registry, fixedServices, env.Allow);
        var failures = recovery.Restore(rig.Plan.ExecId);

        Assert.Empty(failures);
        Assert.Equal(ServiceState.Running, fixedServices.GetState("WIND"));
        Assert.Equal(before.Files, StateOf(env).Files);
        Assert.Equal(before.Registry, StateOf(env).Registry);
        Assert.Equal(BackupStatus.Restored, recovery.Load(rig.Plan.ExecId).Backup!.Status);
        Assert.Empty(recovery.FindUnfinished().Unfinished);
    }

    [Fact]
    public void ServiceStopped_BeforeDeploy_IsLeftStopped_AfterRestoreToo()
    {
        using var env = new TestDeployEnvironment(wind: ServiceState.Stopped);
        File.WriteAllText(Path.Combine(env.Target, "lib"), "i am a file");   // paksa gagal di tengah copy
        var rig = Prepare(env);
        var before = StateOf(env);

        var result = rig.Deployer.Run(rig.Plan);

        Assert.Equal(HistoryOutcome.FailedRestored, result.Outcome);
        AssertSame(before, StateOf(env));
        Assert.Equal(ServiceState.Stopped, env.Services.GetState("WIND"));
        Assert.DoesNotContain(env.Services.Calls, c => c.StartsWith("Start"));
    }

    [Fact]
    public void FailureDetails_AreWrittenToHistory()
    {
        using var env = new TestDeployEnvironment();
        File.WriteAllText(Path.Combine(env.Target, "lib"), "i am a file");
        var rig = Prepare(env);

        rig.Deployer.Run(rig.Plan);

        var record = Assert.Single(rig.History.GetAll().Records);
        Assert.Equal(HistoryOutcome.FailedRestored, record.Outcome);
        Assert.NotEmpty(record.ErrorMessage);
        Assert.Equal(Paths.BackupDir(env.Cache, rig.Plan.ExecId), record.BackupDirectory);
    }
}
