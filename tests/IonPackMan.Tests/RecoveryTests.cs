using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IonPackMan.Deployment;
using IonPackMan.History;
using IonPackMan.Machine;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>Pemulihan setelah crash/mati listrik (PRD R11, tiket T29), seluruhnya pada mesin palsu.</summary>
public class RecoveryTests
{
    private sealed record Snap(SortedDictionary<string, string> Files, Dictionary<string, RegistryValue> Registry, ServiceState Wind, string Ini);

    private static Snap SnapOf(TestDeployEnvironment env) => new(
        TestDeployEnvironment.SnapshotFolder(env.Apps),
        env.Registry.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value),
        env.Services.GetState("WIND"),
        File.ReadAllText(env.AppIni));

    private static void AssertSame(Snap a, Snap b)
    {
        Assert.Equal(a.Files, b.Files);
        Assert.Equal(a.Registry, b.Registry);
        Assert.Equal(a.Wind, b.Wind);
        Assert.Equal(a.Ini, b.Ini);
    }

    /// <summary>
    /// Mensimulasikan aplikasi mati di tengah eksekusi: backup dibuat (InProgress), sebagian perubahan sudah terjadi
    /// (service berhenti, satu file diganti, file baru dibuat, Registry dan INI ditulis), lalu Deployer tidak pernah selesai.
    /// </summary>
    private static DeploymentPlan SimulateCrash(TestDeployEnvironment env)
    {
        var plan = env.Planner.Build(TestDeployEnvironment.All()).Plan!;
        var (backup, errors) = new BackupStore(env.Cache, env.Registry, env.Services, env.Allow).Create(plan);
        Assert.True(backup is not null, string.Join("; ", errors));

        env.Services.Stop("WIND", TimeSpan.FromSeconds(1));
        File.Copy(plan.Files.Single(f => f.RelativePath == "WIND.exe").SourcePath, Path.Combine(env.Target, "WIND.exe"), overwrite: true);
        Directory.CreateDirectory(Path.Combine(env.Target, "lib"));
        File.WriteAllText(Path.Combine(env.Target, "lib", "x.dll"), "half-copied");
        env.Registry.Write(TestDeployEnvironment.RegKey, "Support", RegistryValueType.DWord, "1");
        AppIniEditor.Write(env.AppIni, "WebUI", "Support", "1");
        return plan;
    }

    // "Aplikasi dibuka lagi": semua objek dibuat baru, hanya isi disk yang tersisa.
    private static (RecoveryService Recovery, HistoryStore History, BackupStore Backups) Reopen(TestDeployEnvironment env)
    {
        var backups = new BackupStore(env.Cache, env.Registry, env.Services, env.Allow);
        var history = new HistoryStore(env.Cache);
        return (new RecoveryService(backups, history, env.Machine), history, backups);
    }

    [Fact]
    public void NothingPending_OnACleanMachine()
    {
        using var env = new TestDeployEnvironment();
        var (recovery, _, _) = Reopen(env);

        var (pending, problems) = recovery.FindPending();

        Assert.Empty(pending);
        Assert.Empty(problems);
        Assert.False(recovery.HasPending());
    }

    [Fact]
    public void AfterACrash_TheBackupIsFound_AndRestoreBringsTheMachineBack()
    {
        using var env = new TestDeployEnvironment();
        var before = SnapOf(env);
        var plan = SimulateCrash(env);
        Assert.NotEqual(before.Files, SnapOf(env).Files);   // mesin memang sudah berubah

        var (recovery, history, backups) = Reopen(env);
        var (pending, _) = recovery.FindPending();

        var found = Assert.Single(pending);
        Assert.Equal((plan.ExecId, BackupStatus.InProgress, "wind"), (found.ExecId, found.Status, found.Component));
        Assert.True(recovery.HasPending());

        var result = recovery.Restore(plan.ExecId);

        Assert.True(result.Restored, string.Join("; ", result.Failures));
        Assert.Empty(result.HistoryProblems);
        AssertSame(before, SnapOf(env));
        Assert.False(recovery.HasPending());
        Assert.Equal(BackupStatus.Restored, backups.Load(plan.ExecId).Backup!.Status);

        var record = Assert.Single(history.GetAll().Records);
        Assert.Equal((HistoryOutcome.RestoredAfterCrash, "wind"), (record.Outcome, record.Component));
        Assert.Equal(new[] { "Restore" }, record.Operations);
        Assert.Equal(Paths.BackupDir(env.Cache, plan.ExecId), record.BackupDirectory);
    }

    [Fact]
    public void IncompleteRestore_StaysPending_AndSucceedsOnceTheCauseIsFixed()
    {
        using var env = new TestDeployEnvironment();
        var before = SnapOf(env);
        var plan = SimulateCrash(env);
        env.Registry.FailOn(TestDeployEnvironment.RegKey, "Support");   // Registry menolak tulis, juga saat restore
        var (recovery, history, _) = Reopen(env);

        var first = recovery.Restore(plan.ExecId);

        Assert.False(first.Restored);
        Assert.Contains("registry", string.Join(" ", first.Failures), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HistoryOutcome.FailedRestoreIncomplete, Assert.Single(history.GetAll().Records).Outcome);
        Assert.Equal(BackupStatus.RestoreIncomplete, Assert.Single(recovery.FindPending().Pending).Status);   // masih tertunda

        env.Registry.ClearFailures();
        var second = recovery.Restore(plan.ExecId);

        Assert.True(second.Restored, string.Join("; ", second.Failures));
        AssertSame(before, SnapOf(env));
        Assert.False(recovery.HasPending());
        Assert.Equal(2, history.GetAll().Records.Count);
    }

    [Fact]
    public void UnreadableBackup_IsReported_NotIgnored()
    {
        using var env = new TestDeployEnvironment();
        string dir = Paths.BackupDir(env.Cache, "20261028-101530-wind-3f9a");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "backup.ini"), "[Backup\nbroken");
        var (recovery, _, _) = Reopen(env);

        var (pending, problems) = recovery.FindPending();

        Assert.Empty(pending);
        Assert.NotEmpty(problems);
        Assert.True(recovery.HasPending());   // tetap mengunci sampai dibereskan
    }

    [Fact]
    public void RestoringAnUnknownBackup_FailsClearly_AndTouchesNothing()
    {
        using var env = new TestDeployEnvironment();
        var before = SnapOf(env);
        var (recovery, _, _) = Reopen(env);

        var result = recovery.Restore("20261028-101530-wind-0000");

        Assert.False(result.Restored);
        Assert.NotEmpty(result.Failures);
        AssertSame(before, SnapOf(env));
    }

    [Theory]
    [InlineData("20261028-101530-wind-3f9a", "wind")]
    [InlineData("20261028-101530-non-ndc-parser-0a1b", "non-ndc-parser")]
    [InlineData("garbage", "unknown")]
    [InlineData("a-b", "unknown")]
    public void ComponentIsReadFromTheExecId(string execId, string expected)
    {
        Assert.Equal(expected, RecoveryService.ComponentOf(execId));
    }

    // ---------- Dashboard ----------

    private static DashboardScreen Dashboard(TestDeployEnvironment env, RecoveryService? recovery)
    {
        var reader = new DashboardReader(env.Machine, [], new MachineInspector(), null, env.Profiles, env.Validator, env.Components, new HistoryStore(env.Cache));
        var screen = new DashboardScreen(reader, recovery);
        _ = new Window { Content = screen };
        return screen;
    }

    [Fact]
    public void Dashboard_ShowsTheBanner_AndRestoreClearsIt()
    {
        using var env = new TestDeployEnvironment();
        var before = SnapOf(env);
        SimulateCrash(env);
        var (recovery, _, _) = Reopen(env);

        WpfHost.Run(() =>
        {
            var screen = Dashboard(env, recovery);
            var messages = new List<string>();
            screen.Message += messages.Add;
            screen.Refresh();

            var banner = (FrameworkElement)screen.FindName("RecoveryBanner");
            var restore = (Button)screen.FindName("RestoreButton");
            Assert.Equal(Visibility.Visible, banner.Visibility);
            Assert.True(restore.IsEnabled);
            Assert.Contains("wind", ((TextBlock)screen.FindName("RecoveryText")).Text);

            var failures = screen.RestorePending();

            Assert.Empty(failures);
            Assert.Equal(Visibility.Collapsed, banner.Visibility);
            Assert.Contains("restored", messages.Single(), StringComparison.OrdinalIgnoreCase);
        });
        AssertSame(before, SnapOf(env));
    }

    [Fact]
    public void Dashboard_WithNothingPending_HasNoBanner()
    {
        using var env = new TestDeployEnvironment();
        var (recovery, _, _) = Reopen(env);

        WpfHost.Run(() =>
        {
            var screen = Dashboard(env, recovery);
            screen.Refresh();

            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)screen.FindName("RecoveryBanner")).Visibility);
        });
    }

    [Fact]
    public void Dashboard_WithoutRecoveryService_HasNoBanner()
    {
        using var env = new TestDeployEnvironment();

        WpfHost.Run(() =>
        {
            var screen = Dashboard(env, null);
            screen.Refresh();

            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)screen.FindName("RecoveryBanner")).Visibility);
            Assert.Empty(screen.RestorePending());
        });
    }

    [Fact]
    public void Dashboard_BannerRendersLegibly()
    {
        using var env = new TestDeployEnvironment();
        SimulateCrash(env);
        var (recovery, _, _) = Reopen(env);
        string png = Path.Combine(Path.GetTempPath(), "ionpackman-recovery-banner.png");

        WpfHost.Run(() =>
        {
            var screen = Dashboard(env, recovery);
            screen.Refresh();
            ((Window)screen.Parent).Content = null;
            WpfHost.RenderToPng(screen, 840, png);
        });

        Assert.True(File.Exists(png));
    }

    // ---------- MainWindow ----------

    private sealed class BusyScreen : UserControl, IScreen, IBusyScreen
    {
        public event Action<string>? Message;
        public bool IsBusy { get; set; }
        public void Refresh() { }
        public void Say(string text) => Message?.Invoke(text);
    }

    private static Color DotColor(MainWindow w) => ((SolidColorBrush)((System.Windows.Shapes.Ellipse)w.FindName("StatusDot")).Fill).Color;
    private static Color Brush(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    private static MachineConfig Machine() => new()
    {
        MachineName = "M1", Environment = "Development",
        InstallLocations = new Dictionary<string, string>(), LocalCacheDirectory = @"D:\ION-PackMan\Cache",
    };

    [Fact]
    public void StatusDot_IsAmberWhileABackupIsPending_AndGreenAgain()
    {
        WpfHost.Run(() =>
        {
            bool pending = true;
            var screens = new Dictionary<string, BusyScreen>();
            var w = new MainWindow(key => screens[key] = new BusyScreen(), hasPendingRecovery: () => pending);
            w.SetMachine(Machine(), []);

            Assert.Equal(Brush("Brush.Pending"), DotColor(w));

            pending = false;
            screens["dashboard"].Say("Backup restored.");   // pesan dari layar menyegarkan status

            Assert.Equal(Brush("Brush.Online"), DotColor(w));
        });
    }

    [Fact]
    public void StatusDot_IsRedWhenMachineIniIsBroken_EvenWithAPendingBackup()
    {
        WpfHost.Run(() =>
        {
            var w = new MainWindow(_ => new BusyScreen(), hasPendingRecovery: () => true);
            w.SetMachine(null, ["machine.ini: file not found."]);

            Assert.Equal(Brush("Brush.Red"), DotColor(w));
        });
    }

    [Fact]
    public void Window_RefusesToCloseWhileAScreenIsBusy()
    {
        WpfHost.Run(() =>
        {
            var warnings = new List<string>();
            var screens = new Dictionary<string, BusyScreen>();
            var w = new MainWindow(key => screens[key] = new BusyScreen(), warnBlockedClose: warnings.Add);
            w.SetMachine(Machine(), []);
            w.Navigate(MainWindow.Dashboard);
            w.Show();

            screens["dashboard"].IsBusy = true;
            w.Close();

            Assert.True(w.IsVisible);   // tetap terbuka
            Assert.Contains("still running", Assert.Single(warnings));

            screens["dashboard"].IsBusy = false;
            w.Close();

            Assert.False(w.IsVisible);
        });
    }
}
