using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using IonPackMan.Controls;
using IonPackMan.Deployment;
using IonPackMan.History;
using IonPackMan.Machine;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>Tes Developer Console (T27/T28) pada mesin palsu. Tidak ada tes yang menyentuh Registry, service, atau file nyata.</summary>
public class DevConsoleScreenTests
{
    private const double ContentWidth = 808;

    private static string Png(string name) => Path.Combine(Path.GetTempPath(), $"ionpackman-devconsole-{name}.png");

    private static void ExpectPreviewCleared(DevConsoleTestRig rig, string because)
    {
        Assert.True(rig.Screen.CurrentPlan is null, $"preview still present after {because}");
        Assert.False(rig.ExecuteButton.IsEnabled, $"Execute still enabled after {because}");
        Assert.Equal(Visibility.Visible, rig.Part<EmptyState>("PreviewEmpty").Visibility);
        Assert.Equal(Visibility.Collapsed, rig.Part<FrameworkElement>("PlanPanel").Visibility);
    }

    // ---------- Terkunci ----------

    [Fact]
    public void Production_LocksTheScreen_WithNoForm()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(new TestDeployEnvironment("Production")));
        WpfHost.Run(() =>
        {
            Assert.True(rig.Screen.IsLocked);
            Assert.Equal(Visibility.Collapsed, rig.Part<FrameworkElement>("FormPanel").Visibility);
            Assert.Equal(Visibility.Collapsed, rig.Part<FrameworkElement>("ActionBar").Visibility);
            var empty = rig.Part<EmptyState>("LockedEmpty");
            Assert.Equal(Visibility.Visible, empty.Visibility);
            Assert.Equal("Only available on Development machines", empty.Title);
            Assert.False(rig.Screen.PreviewPlan());
        });
        Assert.Null(rig.Execute().Result);   // ExecutePlanAsync menolak di layar terkunci
    }

    [Fact]
    public void PendingBackup_LocksTheScreen_AndPointsToTheDashboard()
    {
        var env = new TestDeployEnvironment();
        var backups = new BackupStore(env.Cache, env.Registry, env.Services, env.Allow);
        var (plan, errors) = env.Planner.Build(TestDeployEnvironment.All());
        Assert.True(plan is not null, string.Join("; ", errors));
        Assert.NotNull(backups.Create(plan).Backup);   // backup dibuat tetapi tidak pernah ditandai selesai

        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(env));
        WpfHost.Run(() =>
        {
            Assert.True(rig.Screen.IsLocked);
            Assert.Equal(Visibility.Collapsed, rig.Part<FrameworkElement>("FormPanel").Visibility);
            var notice = rig.Part<Notice>("LockedNotice");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal(StatusKind.Error, notice.Kind);
            string text = rig.Part<TextBlock>("LockedNoticeText").Text;
            Assert.Contains("backup", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Dashboard", text);
            Assert.Contains("Restore", text);
        });
    }

    [Fact]
    public void Refresh_UnlocksAgain_OnceTheBackupIsGone()
    {
        var env = new TestDeployEnvironment();
        var backups = new BackupStore(env.Cache, env.Registry, env.Services, env.Allow);
        var plan = env.Planner.Build(TestDeployEnvironment.All()).Plan!;
        var backup = backups.Create(plan).Backup!;

        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(env));
        WpfHost.Run(() => Assert.True(rig.Screen.IsLocked));

        backups.MarkDone(backup);
        WpfHost.Run(() =>
        {
            rig.Screen.Refresh();
            Assert.False(rig.Screen.IsLocked);
            Assert.Equal(Visibility.Visible, rig.Part<FrameworkElement>("FormPanel").Visibility);
        });
    }

    // ---------- Pilihan ----------

    [Fact]
    public void Development_ShowsTheForm_WithDetectedProfile_AndPreviewRequired()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            Assert.False(rig.Screen.IsLocked);
            Assert.Equal("TESTP", rig.Profile.SelectedItem);
            Assert.Equal("detected", rig.Part<TextBlock>("ProfileState").Text);
            Assert.Equal(new[] { "ion-middleware", "wind" }, rig.Component.Items.Cast<string>());
            Assert.True(rig.Deploy.IsChecked);
            Assert.False(rig.Config.IsChecked);
            Assert.False(rig.Restart.IsChecked);
            Assert.False(rig.ExecuteButton.IsEnabled);
            Assert.Equal(Visibility.Visible, rig.Part<EmptyState>("PreviewEmpty").Visibility);
            Assert.Equal("No plan prepared", rig.Part<EmptyState>("PreviewEmpty").Title);
        });
    }

    [Fact]
    public void UnknownProfile_ShowsTheReason_AndRequiresAChoice()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(detected: false));
        WpfHost.Run(() =>
        {
            Assert.Null(rig.Profile.SelectedItem);
            Assert.Equal("not detected", rig.Part<TextBlock>("ProfileState").Text);
            var hint = rig.Part<TextBlock>("ProfileHint");
            Assert.Equal(Visibility.Visible, hint.Visibility);
            Assert.Contains("No profile matches this machine", hint.Text);
            Assert.False(rig.PreviewButton.IsEnabled);
            Assert.Equal("Select a profile", rig.Status);

            rig.Profile.SelectedItem = "TESTP";   // pengguna memilih sendiri
            rig.SelectWind();
            Assert.True(rig.PreviewButton.IsEnabled);
        });
    }

    [Fact]
    public void NoImportedBuild_StillListsTheSavedRelease_DeployNeedsTheBuild_ConfigDoesNot()
    {
        var env = new TestDeployEnvironment(importBuild: false);
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(env));
        WpfHost.Run(() =>
        {
            rig.SelectWind();
            var item = Assert.Single(rig.Build.Items.Cast<object>());
            Assert.Contains("no build imported", item.ToString());   // record BuildItem: ToString memuat labelnya
            Assert.True(rig.Build.IsEnabled);
            Assert.Contains("No build is imported for this version", rig.Part<TextBlock>("BuildHint").Text);

            // Deploy files (dicentang secara default) butuh build: Preview ditolak dengan pesan yang menyuruh import.
            Assert.True(rig.Deploy.IsChecked);
            Assert.False(rig.Screen.PreviewPlan());
            Assert.Contains("Import it in Components first", string.Join(" ", rig.ErrorLines()));

            // Apply config tidak butuh build.
            rig.Deploy.IsChecked = false;
            rig.Config.IsChecked = true;
            Assert.True(rig.Screen.PreviewPlan());
            Assert.True(rig.ExecuteButton.IsEnabled);
        });
    }

    [Fact]
    public void TargetAndService_AreReadOnly_AndComeFromTheComponent()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            rig.SelectWind();
            var target = rig.Part<TextBox>("TargetBox");
            var service = rig.Part<TextBox>("ServiceBox");
            Assert.True(target.IsReadOnly);
            Assert.True(service.IsReadOnly);
            Assert.Equal(rig.Env.Target, target.Text);
            Assert.Equal("WIND", service.Text);

            rig.Component.SelectedItem = "ion-middleware";   // component tanpa service dan tanpa build
            Assert.Equal(Path.Combine(rig.Env.Apps, "Mw"), target.Text);   // tetap dari release component yang tersimpan
            Assert.Equal("(no service)", service.Text);
            Assert.True(target.IsReadOnly && service.IsReadOnly);
        });
    }

    // ---------- Preview ----------

    [Fact]
    public void Preview_Success_ShowsOneSummaryLinePerKind_AndEnablesExecute()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            rig.SelectWind();
            rig.Config.IsChecked = true;
            Assert.True(rig.Screen.PreviewPlan());

            var rows = rig.PlanRows();
            Assert.Equal("1 replace, 2 new, 0 same", rows["Files"]);   // WIND.exe berbeda; config.txt dan lib\x.dll baru
            Assert.Equal("WIND is Running: Stop → Start", rows["Service"]);
            Assert.Contains("Registry · Support: 0 → 1", rows["Config"]);
            Assert.Contains("INI · [WebUI] Support: 0 → 1", rows["Config"]);
            Assert.Contains(Paths.BackupDir(rig.Env.Cache, rig.Screen.CurrentPlan!.ExecId), rows["Backup folder"]);
            Assert.Contains("✔", rows["Guards"]);

            var expander = rig.Part<Expander>("FileListExpander");
            Assert.False(expander.IsExpanded);
            Assert.Equal(3, ((System.Collections.IEnumerable)rig.Part<ItemsControl>("FileList").ItemsSource).Cast<object>().Count());
            Assert.Equal(Visibility.Visible, rig.Part<FrameworkElement>("PlanPanel").Visibility);
            Assert.Equal(Visibility.Collapsed, rig.Part<EmptyState>("PreviewEmpty").Visibility);
            Assert.True(rig.ExecuteButton.IsEnabled);
            Assert.StartsWith("Ready", rig.Status);
        });
    }

    [Fact]
    public void Preview_Failure_ShowsEveryError_AndKeepsExecuteDisabled()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        rig.Env.UnfinishedBackups.AddRange(["old-1 (InProgress)", "old-2 (InProgress)"]);   // Guards menolak: dua alasan
        WpfHost.Run(() =>
        {
            rig.SelectWind();
            Assert.False(rig.Screen.PreviewPlan());

            var notice = rig.Part<Notice>("PreviewErrors");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal(StatusKind.Error, notice.Kind);
            var lines = rig.ErrorLines();
            Assert.Equal(2, lines.Count);
            Assert.Contains("old-1", lines[0]);
            Assert.Contains("old-2", lines[1]);
            Assert.False(rig.ExecuteButton.IsEnabled);
            Assert.Equal("Fix the problems above", rig.Status);
            Assert.Null(rig.Screen.CurrentPlan);
        });
    }

    [Fact]
    public void Preview_Failure_ThenChangingInput_ClearsTheErrors()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        rig.Env.UnfinishedBackups.Add("old (InProgress)");
        WpfHost.Run(() =>
        {
            rig.SelectWind();
            rig.Screen.PreviewPlan();
            Assert.Equal(Visibility.Visible, rig.Part<FrameworkElement>("PreviewErrors").Visibility);

            rig.Config.IsChecked = true;

            Assert.Equal(Visibility.Collapsed, rig.Part<FrameworkElement>("PreviewErrors").Visibility);
            Assert.Equal("Preview required", rig.Status);
        });
    }

    // ---------- Aturan inti: perubahan input menghapus preview ----------

    private static void Previewed(DevConsoleTestRig rig)
    {
        rig.SelectWind();
        Assert.True(rig.Screen.PreviewPlan());
        Assert.True(rig.ExecuteButton.IsEnabled);
    }

    [Fact]
    public void ChangingProfile_ClearsThePreview()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            rig.AddSecondProfile();
            rig.Screen.Refresh();
            Previewed(rig);
            rig.Profile.SelectedItem = "TESTQ";
            ExpectPreviewCleared(rig, "changing the profile");
            Assert.Equal("Preview required", rig.Status);
        });
    }

    [Fact]
    public void ChangingComponent_ClearsThePreview()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            Previewed(rig);
            rig.Component.SelectedItem = "ion-middleware";
            ExpectPreviewCleared(rig, "changing the component");
        });
    }

    [Fact]
    public void ChangingBuild_ClearsThePreview()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            rig.AddSecondBuild();
            rig.Screen.Refresh();
            Previewed(rig);
            Assert.Equal(2, rig.Build.Items.Count);
            rig.Build.SelectedValue = rig.Build.SelectedValue as string == "2.2.0" ? "2.1.0" : "2.2.0";
            ExpectPreviewCleared(rig, "changing the build");
        });
    }

    [Theory]
    [InlineData("DeployBox")]
    [InlineData("ConfigBox")]
    [InlineData("RestartBox")]
    public void TogglingAnOperation_ClearsThePreview(string checkbox)
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            Previewed(rig);
            var box = rig.Part<CheckBox>(checkbox);
            box.IsChecked = box.IsChecked != true;
            ExpectPreviewCleared(rig, $"toggling {checkbox}");
        });
    }

    [Fact]
    public void WithNoOperationSelected_PreviewIsDisabled_WithAReason()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            rig.SelectWind();
            rig.Deploy.IsChecked = false;
            Assert.False(rig.PreviewButton.IsEnabled);
            Assert.Equal("Select at least one operation", rig.Status);
        });
    }

    [Fact]
    public void Refresh_KeepsTheSelection_ButDropsTheOldPlan()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            Previewed(rig);
            rig.Screen.Refresh();
            Assert.Equal("wind", rig.Component.SelectedItem);
            Assert.Equal("2.1.0", rig.Build.SelectedValue);
            ExpectPreviewCleared(rig, "Refresh");
        });
    }

    // ---------- Preselect ("Use in Developer Console") ----------

    [Fact]
    public void Preselect_FillsProfileComponentAndBuild_AndClearsThePreview()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(detected: false));
        WpfHost.Run(() =>
        {
            rig.Profile.SelectedItem = "TESTP";   // detektor tidak menemukan apa pun: pengguna memilih sendiri
            rig.SelectWind();
            Assert.True(rig.Screen.PreviewPlan());

            rig.Component.SelectedItem = "ion-middleware";
            rig.Screen.Preselect("wind", "2.1.0");

            Assert.Equal("TESTP", rig.Profile.SelectedItem);
            Assert.Equal("wind", rig.Component.SelectedItem);
            Assert.Equal("2.1.0", rig.Build.SelectedValue);
            Assert.Equal(rig.Env.Target, rig.Part<TextBox>("TargetBox").Text);
            Assert.Equal("WIND", rig.Part<TextBox>("ServiceBox").Text);
            ExpectPreviewCleared(rig, "Preselect");
        });
    }

    [Fact]
    public void Preselect_WithoutAnyProfileHoldingTheComponent_SendsAMessage_AndChangesNothing()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            rig.SelectWind();
            rig.Screen.Preselect("unknown-component", "1.0.0");
            Assert.Equal("wind", rig.Component.SelectedItem);
            Assert.Contains("No profile contains", Assert.Single(rig.Messages));
        });
    }

    [Fact]
    public void Preselect_OnALockedScreen_DoesNothing()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(new TestDeployEnvironment("Production")));
        WpfHost.Run(() =>
        {
            rig.Screen.Preselect("wind", "2.1.0");
            Assert.True(rig.Screen.IsLocked);
            Assert.Empty(rig.Messages);
        });
    }

    // ---------- Eksekusi ----------

    [Fact]
    public void Execute_Succeeds_ChangesTheFakeMachine_AndShowsTheResultCard()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() => { rig.SelectWind(); rig.Config.IsChecked = true; Assert.True(rig.Screen.PreviewPlan()); });

        var (result, steps, busy) = rig.Execute();

        Assert.NotNull(result);
        Assert.Equal(HistoryOutcome.Succeeded, result.Outcome);
        Assert.Equal("new-exe", File.ReadAllText(Path.Combine(rig.Env.Target, "WIND.exe")));
        Assert.Equal("1", rig.Env.Registry.Read(TestDeployEnvironment.RegKey, "Support")!.Value);
        Assert.Equal(ServiceState.Running, rig.Env.Services.GetState("WIND"));
        Assert.Equal(DeployStep.Checking, steps[0]);
        Assert.Equal(DeployStep.Finalizing, steps[^1]);
        Assert.All(busy, b => Assert.True(b));   // IsBusy true selama eksekusi berjalan

        WpfHost.Run(() =>
        {
            Assert.False(rig.Screen.IsBusy);
            Assert.Same(result, rig.Screen.LastResult);
            Assert.Equal(Visibility.Visible, rig.Part<FrameworkElement>("ResultPanel").Visibility);
            Assert.Equal(StatusKind.Success, rig.Part<Notice>("ResultNotice").Kind);
            Assert.Contains("Execution succeeded", rig.ResultText());
            Assert.True(rig.Part<Button>("OpenBackupButton").IsEnabled);
            Assert.False(rig.ExecuteButton.IsEnabled);        // satu plan, satu eksekusi
            Assert.True(rig.Screen.CurrentPlan is null);
            Assert.Contains("Execution succeeded.", rig.Messages);
            Assert.True(rig.Part<FrameworkElement>("FormPanel").IsEnabled);
        });
        Assert.Equal(HistoryOutcome.Succeeded, Assert.Single(rig.History.GetAll().Records).Outcome);
    }

    [Fact]
    public void Execute_Failure_RestoresTheMachine_AndShowsTheErrorCard()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        // "lib" adalah FILE di target, jadi lib\x.dll tidak bisa disalin di tengah copy (seperti DeployerTests).
        File.WriteAllText(Path.Combine(rig.Env.Target, "lib"), "i am a file");
        var before = TestDeployEnvironment.SnapshotFolder(rig.Env.Apps);
        WpfHost.Run(() => { rig.SelectWind(); rig.Config.IsChecked = true; Assert.True(rig.Screen.PreviewPlan()); });

        var (result, steps, busy) = rig.Execute();

        Assert.Equal(HistoryOutcome.FailedRestored, result!.Outcome);
        Assert.Equal(before, TestDeployEnvironment.SnapshotFolder(rig.Env.Apps));
        Assert.Equal("0", rig.Env.Registry.Read(TestDeployEnvironment.RegKey, "Support")!.Value);
        Assert.All(busy, b => Assert.True(b));
        Assert.Contains(DeployStep.Restoring, steps);   // dialog progres akan menampilkan "Restoring from backup…"

        WpfHost.Run(() =>
        {
            Assert.False(rig.Screen.IsBusy);
            var notice = rig.Part<Notice>("ResultNotice");
            Assert.Equal(StatusKind.Error, notice.Kind);
            string text = rig.ResultText();
            Assert.Contains("restored from the backup", text);
            Assert.Contains(result.Errors[0], text);
            Assert.Equal(Visibility.Visible, rig.Part<FrameworkElement>("ResultPanel").Visibility);
            Assert.Equal(Visibility.Visible, rig.Part<FrameworkElement>("OpenBackupButton").Visibility);
            Assert.False(rig.ExecuteButton.IsEnabled);
            Assert.False(rig.Screen.IsLocked);   // restore lengkap: konsol tidak perlu pemulihan
        });
    }

    [Fact]
    public void Execute_WithoutAValidPlan_DoesNothing()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        var before = TestDeployEnvironment.SnapshotFolder(rig.Env.Apps);

        var (result, steps, _) = rig.Execute();

        Assert.Null(result);
        Assert.Empty(steps);
        Assert.Equal(before, TestDeployEnvironment.SnapshotFolder(rig.Env.Apps));
    }

    [Fact]
    public void Execute_Rejected_WhenGuardsFailAtExecuteTime_ShowsAWarningCard_AndChangesNothing()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() => { rig.SelectWind(); Assert.True(rig.Screen.PreviewPlan()); });
        File.WriteAllText(Path.Combine(rig.Screen.CurrentPlan!.BuildFilesDirectory, "config.txt"), "changed-after-preview");
        var before = TestDeployEnvironment.SnapshotFolder(rig.Env.Apps);

        var (result, _, _) = rig.Execute();

        Assert.Equal(HistoryOutcome.Rejected, result!.Outcome);
        Assert.Equal(before, TestDeployEnvironment.SnapshotFolder(rig.Env.Apps));
        WpfHost.Run(() =>
        {
            Assert.Equal(StatusKind.Warning, rig.Part<Notice>("ResultNotice").Kind);
            Assert.Contains("Nothing was changed", rig.ResultText());
        });
    }

    [Fact]
    public void WhileExecuting_TheFormIsLocked_AndASecondExecuteIsRefused()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() => { rig.SelectWind(); Assert.True(rig.Screen.PreviewPlan()); });

        bool formEnabledDuring = true, executeEnabledDuring = true, previewEnabledDuring = true;
        Task<DeploymentResult?>? second = null;
        var progress = new InlineProgress(step =>
        {
            if (step != DeployStep.CopyingFiles) return;
            WpfHost.Run(() =>
            {
                formEnabledDuring = rig.Part<FrameworkElement>("FormPanel").IsEnabled;
                executeEnabledDuring = rig.ExecuteButton.IsEnabled;
                previewEnabledDuring = rig.PreviewButton.IsEnabled;
                second = rig.Screen.ExecutePlanAsync();
            });
        });

        var task = WpfHost.Run(() => rig.Screen.ExecutePlanAsync(progress));
        Assert.True(task.Wait(TimeSpan.FromSeconds(30)));

        Assert.False(formEnabledDuring);
        Assert.False(executeEnabledDuring);
        Assert.False(previewEnabledDuring);
        Assert.Null(second!.Result);
        Assert.Equal(HistoryOutcome.Succeeded, task.Result!.Outcome);
    }

    private sealed class InlineProgress(Action<DeployStep> onReport) : IProgress<DeployStep>
    {
        public void Report(DeployStep value) => onReport(value);
    }

    // ---------- Tampilan: PNG dan lebar ----------

    private static void RenderAndCheck(DevConsoleTestRig rig, string name)
    {
        string png = Png(name);
        CheckLayout(rig);
        rig.Host.Content = null;   // RenderToPng memasang layar di jendelanya sendiri
        var (width, height) = WpfHost.RenderToPng(rig.Screen, ContentWidth + 32, png);
        Assert.True(new FileInfo(png).Length > 5_000, $"{name}: PNG was not written");
        Assert.True(height > 200);
    }

    // Layar ditampilkan di jendela di luar layar selebar area konten (808) + margin 2x16, lalu tiap elemen diperiksa.
    private static void CheckLayout(DevConsoleTestRig rig)
    {
        var host = rig.Host;
        host.Content = null;
        var holder = new Border { Padding = new Thickness(16), Child = rig.Screen };
        host.Content = holder;
        host.WindowStyle = WindowStyle.None; host.ShowInTaskbar = false; host.ShowActivated = false;
        host.Left = -20000; host.Top = -20000; host.Width = ContentWidth + 32; host.SizeToContent = SizeToContent.Height;
        host.Show();
        host.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        host.UpdateLayout();
        try { AssertNothingOverflowsHorizontally(rig.Screen, ContentWidth); }
        finally { holder.Child = null; host.Close(); }
    }

    // Tidak boleh ada elemen yang tampil melewati tepi kanan konten (isi TextBox/ScrollViewer di-clip, jadi dikecualikan).
    private static void AssertNothingOverflowsHorizontally(FrameworkElement screen, double maxWidth)
    {
        Assert.True(screen.ActualWidth <= maxWidth + 0.5, $"screen is {screen.ActualWidth}px wide, more than {maxWidth}px");
        foreach (var element in WpfHost.Descendants<FrameworkElement>(screen))
        {
            if (!element.IsVisible || element.ActualWidth == 0 || HasAncestor<TextBoxBase>(element) || HasAncestor<ComboBox>(element))
                continue;
            double right = element.TranslatePoint(new Point(element.ActualWidth, 0), screen).X;
            Assert.True(right <= screen.ActualWidth + 1, $"{element.GetType().Name} {element.Name} ends at {right:0}px, outside {screen.ActualWidth:0}px");
        }
    }

    private static bool HasAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(node); p is not null; p = System.Windows.Media.VisualTreeHelper.GetParent(p))
            if (p is T) return true;
        return false;
    }

    // Action bar lengket: di dalam MainWindow (layar ada di ScrollViewer) bar tetap di dasar viewport dan jendela tidak scroll.
    [Theory]
    [InlineData(false, 768)]
    [InlineData(true, 768)]
    [InlineData(true, 620)]
    public void InsideMainWindow_TheActionBarStaysInView_WithoutScrollingTheWindow(bool previewed, double windowHeight)
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            rig.Host.Content = null;
            var window = new MainWindow(key => key == MainWindow.DevConsole ? rig.Screen : new PlaceholderScreen("x", "y"));
            try
            {
                window.Navigate(MainWindow.DevConsole);
                rig.SelectWind();
                rig.Config.IsChecked = true;
                if (previewed) rig.Screen.PreviewPlan();
                WpfHost.RenderWindowToPng(window, 1024, windowHeight, Png($"8-mainwindow-{(previewed ? "preview" : "empty")}-{windowHeight}"));
                window.UpdateLayout();

                var scroll = (ScrollViewer)window.FindName("ContentScroll");
                Assert.Equal(Visibility.Collapsed, scroll.ComputedVerticalScrollBarVisibility);   // jendela tidak perlu scroll
                var bar = rig.Part<FrameworkElement>("ActionBar");
                double bottom = bar.TranslatePoint(new Point(0, bar.ActualHeight), scroll).Y;
                Assert.InRange(bottom, scroll.ActualHeight - 20, scroll.ActualHeight);              // menempel di dasar viewport
                Assert.True(rig.ExecuteButton.IsVisible && rig.PreviewButton.IsVisible);
                // Isi di atas bar yang scroll sendiri
                var body = rig.Part<ScrollViewer>("BodyScroll");
                if (previewed && windowHeight < 700)
                    Assert.NotEqual(Visibility.Collapsed, body.ComputedVerticalScrollBarVisibility);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Render_Locked_Production()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(new TestDeployEnvironment("Production")));
        WpfHost.Run(() => RenderAndCheck(rig, "1-locked-production"));
    }

    [Fact]
    public void Render_Empty()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() => { rig.SelectWind(); RenderAndCheck(rig, "2-empty"); });
    }

    [Fact]
    public void Render_PreviewSuccess()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() =>
        {
            rig.SelectWind();
            rig.Config.IsChecked = true;
            rig.Screen.PreviewPlan();
            RenderAndCheck(rig, "3-preview-success");
        });
    }

    [Fact]
    public void Render_PreviewFailure()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        rig.Env.UnfinishedBackups.AddRange(["old-1 (InProgress)", "old-2 (InProgress)"]);
        WpfHost.Run(() => { rig.SelectWind(); rig.Screen.PreviewPlan(); RenderAndCheck(rig, "4-preview-failure"); });
    }

    [Fact]
    public void Render_ResultSuccess()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        WpfHost.Run(() => { rig.SelectWind(); rig.Config.IsChecked = true; rig.Screen.PreviewPlan(); });
        rig.Execute();
        WpfHost.Run(() => RenderAndCheck(rig, "5-result-success"));
    }

    [Fact]
    public void Render_ResultFailure()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        File.WriteAllText(Path.Combine(rig.Env.Target, "lib"), "i am a file");
        WpfHost.Run(() => { rig.SelectWind(); rig.Config.IsChecked = true; rig.Screen.PreviewPlan(); });
        rig.Execute();
        WpfHost.Run(() => RenderAndCheck(rig, "6-result-failure"));
    }

    [Fact]
    public void Render_UnknownProfile()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(detected: false));
        WpfHost.Run(() => RenderAndCheck(rig, "7-unknown-profile"));
    }
}
