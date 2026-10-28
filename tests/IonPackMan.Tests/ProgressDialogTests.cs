using System.Windows;
using System.Windows.Controls;
using IonPackMan.Deployment;
using IonPackMan.History;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>Tes ProgressDialog (T28). Dialog dibuka tanpa ShowDialog (Show di luar layar) supaya tes tidak terblokir.</summary>
public class ProgressDialogTests
{
    private static DeploymentPlan Plan(ServiceState service = ServiceState.Running, bool deploy = true, bool config = true, bool restart = false) => new()
    {
        ExecId = "20260101-000000-wind", ProfileId = "P", ComponentId = "wind", ReleaseVersion = "2.1.0",
        TargetDirectory = @"D:\apps\WIND", ServiceName = "WIND", ServiceStateAtPlan = service,
        DeployFiles = deploy, ApplyConfig = config, RestartService = restart,
    };

    private static DeploymentResult Result(HistoryOutcome outcome, string[]? errors = null, string[]? restoreFailures = null) =>
        new(outcome, "id", errors ?? [], restoreFailures ?? [], @"D:\cache\Backups\id", []);

    private static ProgressDialog Open(DeploymentPlan plan)
    {
        var dialog = new ProgressDialog(plan, "TEST-PC")
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false,
        };
        dialog.Show();
        return dialog;
    }

    private static string[] Glyphs(ProgressDialog d) =>
        ((StackPanel)d.FindName("StepsPanel")).Children.OfType<StackPanel>().Select(p => ((TextBlock)p.Children[0]).Text).ToArray();

    [Fact]
    public void ShowsOnlyTheStepsThatApplyToThePlan()
    {
        WpfHost.Run(() =>
        {
            DeployStep[] Steps(DeploymentPlan plan) => new ProgressDialog(plan).Steps.Select(s => s.Step).ToArray();

            Assert.Equal(new[] { DeployStep.Checking, DeployStep.BackingUp, DeployStep.StoppingService, DeployStep.CopyingFiles,
                                 DeployStep.ApplyingConfig, DeployStep.StartingService, DeployStep.Finalizing }, Steps(Plan()));

            // Service berhenti: tidak ada Stop/Start otomatis.
            Assert.Equal(new[] { DeployStep.Checking, DeployStep.BackingUp, DeployStep.CopyingFiles, DeployStep.ApplyingConfig, DeployStep.Finalizing },
                Steps(Plan(ServiceState.Stopped)));

            Assert.Equal(new[] { DeployStep.Checking, DeployStep.BackingUp, DeployStep.ApplyingConfig, DeployStep.Finalizing },
                Steps(Plan(deploy: false, config: true)));

            Assert.Equal(new[] { DeployStep.Checking, DeployStep.BackingUp, DeployStep.StoppingService, DeployStep.StartingService, DeployStep.Finalizing },
                Steps(Plan(deploy: false, config: false, restart: true)));
        });
    }

    [Fact]
    public void Report_MarksEarlierStepsDone_AndTheCurrentOneRunning()
    {
        WpfHost.Run(() =>
        {
            var dialog = new ProgressDialog(Plan());
            Assert.Equal(StepState.Running, dialog.Steps[0].State);   // langsung mulai dari Checking

            dialog.Report(DeployStep.CopyingFiles);

            var states = dialog.Steps.Select(s => s.State).ToArray();
            Assert.Equal(new[] { StepState.Done, StepState.Done, StepState.Done, StepState.Running, StepState.Pending, StepState.Pending, StepState.Pending }, states);
            Assert.Equal(new[] { "✔", "✔", "✔", "▶", "○", "○", "○" }, Glyphs(dialog));
        });
    }

    [Fact]
    public void Report_FromABackgroundThread_IsMarshalledToTheUiThread()
    {
        var dialog = WpfHost.Run(() => new ProgressDialog(Plan()));

        Task.Run(() =>
        {
            dialog.Report(DeployStep.StoppingService);
            dialog.Report(DeployStep.ApplyingConfig);
        }).Wait();
        WpfHost.Run(() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle));   // antrean Report diproses lebih dulu

        WpfHost.Run(() => Assert.Equal(StepState.Running, dialog.Steps.Single(s => s.Step == DeployStep.ApplyingConfig).State));
    }

    [Fact]
    public void ProgressCreatedOnTheUiThread_DrivesTheDialog_WithARealDeployer()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        var plan = rig.Env.Planner.Build(TestDeployEnvironment.All()).Plan!;
        var dialog = WpfHost.Run(() => new ProgressDialog(plan));

        // Progress<T> dibuat di thread UI: laporan dari thread pool kembali ke thread UI.
        var progress = WpfHost.Run(() => new Progress<DeployStep>(dialog.Report));
        var result = Task.Run(() => rig.Deployer.Run(plan, progress)).Result;
        WpfHost.Run(() => dialog.Complete(result));

        WpfHost.Run(() =>
        {
            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
            Assert.All(dialog.Steps, s => Assert.Equal(StepState.Done, s.State));
            Assert.False(dialog.IsRunning);
            Assert.True(dialog.IsCloseButtonVisible);
        });
    }

    [Fact]
    public void CannotBeClosed_WhileRunning_AndTheCloseButtonIsHidden()
    {
        WpfHost.Run(() =>
        {
            var dialog = Open(Plan());
            try
            {
                Assert.True(dialog.IsRunning);
                Assert.False(dialog.IsCloseButtonVisible);

                dialog.Close();   // sama dengan tombol X / Alt+F4 (Closing dibatalkan)

                Assert.True(dialog.IsVisible);
            }
            finally { dialog.Complete(Result(HistoryOutcome.Succeeded)); dialog.Close(); }
        });
    }

    [Fact]
    public void AfterCompletion_TheCloseButtonAppears_AndTheDialogCanClose()
    {
        WpfHost.Run(() =>
        {
            var dialog = Open(Plan());

            dialog.Complete(Result(HistoryOutcome.Succeeded));

            Assert.False(dialog.IsRunning);
            Assert.True(dialog.IsCloseButtonVisible);
            Assert.Contains("successfully", dialog.Summary);
            Assert.All(Glyphs(dialog), g => Assert.Equal("✔", g));

            dialog.Close();
            Assert.False(dialog.IsVisible);
        });
    }

    [Fact]
    public void Restoring_ShowsTheRestoreMessage_AndMarksTheFailedStep()
    {
        WpfHost.Run(() =>
        {
            var dialog = new ProgressDialog(Plan());
            dialog.Report(DeployStep.CopyingFiles);

            dialog.Report(DeployStep.Restoring);

            Assert.Equal("Restoring from backup…", dialog.Summary);
            Assert.Equal(StepState.Failed, dialog.Steps.Single(s => s.Step == DeployStep.CopyingFiles).State);
            Assert.True(dialog.IsRunning);   // pemulihan juga tidak boleh diputus

            dialog.Complete(Result(HistoryOutcome.FailedRestored, ["boom"]));
            Assert.Contains("restored from the backup", dialog.Summary);
            Assert.Equal(StepState.Failed, dialog.Steps.Single(s => s.Step == DeployStep.CopyingFiles).State);
            Assert.Equal(StepState.Pending, dialog.Steps.Single(s => s.Step == DeployStep.Finalizing).State);
        });
    }

    [Theory]
    [InlineData(HistoryOutcome.FailedRestoreIncomplete, "incomplete")]
    [InlineData(HistoryOutcome.Rejected, "Nothing was changed")]
    public void Complete_DescribesEveryOutcome(HistoryOutcome outcome, string expected)
    {
        WpfHost.Run(() =>
        {
            var dialog = new ProgressDialog(Plan());
            dialog.Complete(Result(outcome, ["x"], outcome == HistoryOutcome.FailedRestoreIncomplete ? ["lib"] : null));
            Assert.Contains(expected, dialog.Summary);
            Assert.True(dialog.IsCloseButtonVisible);
        });
    }

    [Fact]
    public void LateReports_AfterCompletion_DoNotOverwriteTheResult()
    {
        WpfHost.Run(() =>
        {
            var dialog = new ProgressDialog(Plan());
            dialog.Complete(Result(HistoryOutcome.Succeeded));
            dialog.Report(DeployStep.Restoring);
            dialog.Report(DeployStep.Checking);
            Assert.All(dialog.Steps, s => Assert.Equal(StepState.Done, s.State));
            Assert.Contains("successfully", dialog.Summary);
        });
    }

    [Fact]
    public void Size_Is480x320_AndWritesPreviewPngs()
    {
        WpfHost.Run(() =>
        {
            var running = new ProgressDialog(Plan(), "TEST-PC");
            Assert.Equal(480, running.Width);
            Assert.Equal(320, running.Height);
            Assert.Equal(WindowStartupLocation.CenterOwner, running.WindowStartupLocation);
            Assert.False(running.ShowInTaskbar);
            Assert.Equal(ResizeMode.NoResize, running.ResizeMode);

            running.Report(DeployStep.CopyingFiles);
            WpfHost.RenderWindowToPng(running, 464, 281, Path.Combine(Path.GetTempPath(), "ionpackman-progress-running.png"));
            running.Complete(Result(HistoryOutcome.Succeeded));
            WpfHost.RenderWindowToPng(running, 464, 281, Path.Combine(Path.GetTempPath(), "ionpackman-progress-done.png"));
            running.Close();

            var failed = new ProgressDialog(Plan(), "TEST-PC");
            failed.Report(DeployStep.CopyingFiles);
            failed.Report(DeployStep.Restoring);
            WpfHost.RenderWindowToPng(failed, 464, 281, Path.Combine(Path.GetTempPath(), "ionpackman-progress-restoring.png"));
            failed.Complete(Result(HistoryOutcome.FailedRestored, ["boom"]));
            WpfHost.RenderWindowToPng(failed, 464, 281, Path.Combine(Path.GetTempPath(), "ionpackman-progress-failed.png"));
            failed.Close();
        });
    }
}
