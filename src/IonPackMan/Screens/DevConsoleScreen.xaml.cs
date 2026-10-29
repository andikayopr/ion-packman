using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IonPackMan.Builds;
using IonPackMan.Components;
using IonPackMan.Controls;
using IonPackMan.Deployment;
using IonPackMan.History;
using IonPackMan.Machine;
using IonPackMan.Profiles;

namespace IonPackMan.Screens;

/// <summary>
/// Developer Console (R8-R11): pilih -> Preview -> Execute. Layar ini hanya mengunci alur dan menampilkan hasil;
/// semua pemeriksaan keselamatan tetap dilakukan Guards (di PlanBuilder dan lagi di Deployer).
/// Aturan inti (R10): setiap perubahan input menghapus preview dan menonaktifkan Execute.
/// </summary>
public partial class DevConsoleScreen : UserControl, IScreen, IBusyScreen
{
    /// <summary>Isi Imported build ComboBox: versi build yang ada di local cache.</summary>
    public sealed record BuildItem(string Version, string Text)
    {
        public override string ToString() => Text;   // kotak pilihan ComboBox menampilkan ToString()
    }

    private sealed record FileRow(string Change, string Path, string Size, Brush Brush);

    private const double MinScreenHeight = 440;

    private readonly MachineConfig _machine;
    private readonly ProfileStore _profiles;
    private readonly ComponentStore _components;
    private readonly BuildImporter _builds;
    private readonly PlanBuilder _planner;
    private readonly Deployer _deployer;
    private readonly BackupStore _backups;
    private readonly ProfileDetector? _detector;
    private readonly ExecutionLock _gate;
    private readonly ClientConfig _clients;

    private bool _updating = true;         // true selama kontrol diisi dari kode (bukan perubahan oleh pengguna)
    private bool _locked;
    private bool _recoveryNeeded;
    private DeploymentPlan? _plan;
    private List<string> _errors = [];
    private DeploymentResult? _result;
    private string? _detectedProfile;
    private string? _profileProblem;
    private ScrollViewer? _hostScroll;

    /// <summary>Pesan singkat untuk Toast di MainWindow.</summary>
    public event Action<string>? Message;

    public DevConsoleScreen(MachineConfig machine, ProfileStore profiles, ComponentStore components, BuildImporter builds,
        PlanBuilder planner, Deployer deployer, BackupStore backups, ProfileDetector? detector, ExecutionLock? gate = null, ClientConfig? clients = null)
    {
        _gate = gate ?? new ExecutionLock();
        _clients = clients ?? ClientConfig.None;
        _machine = machine;
        _profiles = profiles;
        _components = components;
        _builds = builds;
        _planner = planner;
        _deployer = deployer;
        _backups = backups;
        _detector = detector;

        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Refresh();
    }

    /// <summary>True selama eksekusi berjalan; MainWindow menolak ditutup selama itu.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>True bila layar terkunci (bukan Development, atau ada backup tertunda): form tidak tampil.</summary>
    public bool IsLocked => _locked;

    /// <summary>Plan hasil Preview terakhir yang valid; null bila belum ada atau sudah dihapus.</summary>
    public DeploymentPlan? CurrentPlan => _plan;
    /// <summary>Hasil eksekusi terakhir yang sedang ditampilkan di card Execution Preview.</summary>
    public DeploymentResult? LastResult => _result;

    // ---------- Muat ulang / kunci ----------

    /// <summary>Membaca ulang daftar profile/component/build dan memeriksa kunci. Pilihan yang masih valid dipertahankan; preview dihapus.</summary>
    public void Refresh()
    {
        if (IsBusy)
            return;

        _updating = true;
        try
        {
            var pending = AppServices.PendingBackupLines(_backups);
            _recoveryNeeded = false;

            if (!_machine.IsDevelopment)
                Lock("Only available on Development machines",
                    $"This machine's Environment is '{_machine.Environment}'. Deployments can only run on Development machines.", null);
            else if (_gate.IsHeld)
                Lock("An execution is running",
                    "A profile switch is changing this machine right now. Open this screen again after it finishes.", null);
            else if (pending.Count > 0)
                Lock("Recovery needed", null,
                    "A previous execution did not finish and left a backup that must be restored first: " + string.Join("; ", pending) +
                    ". The Developer Console stays locked until then. Open the Dashboard and press Restore.");
            else
                Unlock();

            if (!_locked)
            {
                LoadProfiles();
                LoadComponents(previous: null);
                LoadBuilds(previous: null);
                ShowTarget();
            }
        }
        finally
        {
            _updating = false;
        }
        ClearPreview();
    }

    private void Lock(string title, string? message, string? error)
    {
        _locked = true;
        FormPanel.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        LockedCard.Visibility = Visibility.Visible;
        LockedEmpty.Title = title;
        LockedEmpty.Message = message ?? "";
        LockedEmpty.Visibility = error is null ? Visibility.Visible : Visibility.Collapsed;
        LockedNotice.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        LockedNoticeText.Text = error ?? "";
    }

    private void Unlock()
    {
        _locked = false;
        FormPanel.Visibility = Visibility.Visible;
        ActionBar.Visibility = Visibility.Visible;
        LockedCard.Visibility = Visibility.Collapsed;
    }

    // ---------- Pilihan: profile -> component -> build -> target ----------

    private string? SelectedProfile => ProfileBox.SelectedItem as string;
    private string? SelectedComponent => ComponentBox.SelectedItem as string;
    private string? SelectedVersion => BuildBox.SelectedValue as string;

    private void LoadProfiles()
    {
        string? previous = SelectedProfile;
        var all = _profiles.ListIds();

        var detection = _detector?.Detect(_machine);
        _detectedProfile = detection?.ProfileId is { } d ? all.FirstOrDefault(i => i.Equals(d, StringComparison.OrdinalIgnoreCase)) : null;

        // Developer Console mengerjakan patch per component di profile aktif; pindah profile ada di layar Profiles.
        // Profile aktif belum diketahui: pengguna wajib memilih sendiri (PRD R15).
        var ids = _detectedProfile is not null ? [_detectedProfile] : all;
        ProfileBox.ItemsSource = ids;

        if (previous is not null && ids.Contains(previous))
            ProfileBox.SelectedItem = previous;
        else if (_detectedProfile is not null)
            ProfileBox.SelectedItem = _detectedProfile;
        else
            ProfileBox.SelectedIndex = -1;   // tidak pasti: pengguna WAJIB memilih

        if (detection is null)
        {
            ProfileState.Text = "";
            ProfileHint.Visibility = Visibility.Collapsed;
        }
        else if (_detectedProfile is not null)
        {
            ProfileState.Text = "detected";
            ProfileHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            ProfileState.Text = "not detected";
            ProfileHint.Text = detection.Reason + " Choose a profile.";
            ProfileHint.ToolTip = ProfileHint.Text;
            ProfileHint.Visibility = Visibility.Visible;
        }
    }

    private void LoadComponents(string? previous)
    {
        previous ??= SelectedComponent;
        _profileProblem = null;
        var ids = new List<string>();
        if (SelectedProfile is { } profileId)
        {
            var (profile, errors) = _profiles.Load(profileId);
            if (profile is not null)
                ids = profile.Components.Select(c => c.Id).Distinct().ToList();
            else
                _profileProblem = $"Profile '{profileId}' cannot be loaded: {errors.FirstOrDefault() ?? "unknown problem"}";
        }
        ComponentBox.ItemsSource = ids;
        if (previous is not null && ids.Contains(previous))
            ComponentBox.SelectedItem = previous;
        else
            ComponentBox.SelectedIndex = ids.Count > 0 ? 0 : -1;
    }

    // Versi diambil dari release component yang tersimpan, bukan hanya dari build yang diimpor: Apply config dan
    // Restart service tidak butuh build. Build baru wajib bila Deploy files dicentang (PlanBuilder menolaknya dengan pesan jelas).
    private HashSet<string> _importedVersions = [];

    // Build yang boleh dipakai untuk component ini di mesin ber-Client (ADR-006 §4): "" = default.
    private string BuildClient => SelectedComponent is { } id ? _clients.BuildClientFor(_machine.Client, id) : "";
    private string BuildKind => BuildClient.Length == 0 ? "" : BuildClient + " ";

    private void LoadBuilds(string? previous)
    {
        previous ??= SelectedVersion;
        var items = new List<BuildItem>();
        _importedVersions = [];
        if (SelectedComponent is { } componentId)
        {
            string client = BuildClient;
            var imported = _builds.ListBuilds()
                .Where(b => b.ComponentId == componentId && b.Client.Equals(client, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(b => b.ReleaseVersion);
            _importedVersions = [.. imported.Keys];
            items = _components.ListVersions(componentId)
                .Select(v => new BuildItem(v, imported.TryGetValue(v, out var build) ? $"{v} · {build.Display}" : $"{v} · no {BuildKind}build imported"))
                .ToList();
        }

        BuildBox.ItemsSource = items;
        BuildBox.IsEnabled = items.Count > 0;

        string? profileVersion = null;
        if (SelectedProfile is { } profileId && SelectedComponent is { } comp)
            profileVersion = _profiles.Load(profileId).Profile?.Components.FirstOrDefault(c => c.Id == comp)?.Version;

        if (items.Count == 0)
        {
            BuildBox.SelectedIndex = -1;
            return;
        }

        string chosen = items.Any(i => i.Version == previous) ? previous!
            : items.Any(i => i.Version == profileVersion) ? profileVersion!
            : items.FirstOrDefault(i => _importedVersions.Contains(i.Version))?.Version ?? items[0].Version;
        BuildBox.SelectedValue = chosen;
    }

    private void UpdateBuildHint(string? releaseProblem)
    {
        string? text = _profileProblem ?? releaseProblem
            ?? (SelectedComponent is not null && BuildBox.Items.Count == 0 ? "Save a component release in Components first." : null)
            ?? (SelectedVersion is { } version && !_importedVersions.Contains(version)
                ? $"No {BuildKind}build is imported for this version. That is fine for Apply config or Restart service; Deploy files needs one (import it in Components)."
                : null);
        BuildHint.Text = text ?? "";
        BuildHint.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateClientHint();
    }

    // Menjelaskan build mana yang dipakai component ini di mesin ber-Client, supaya developer tidak perlu menebak (ADR-006).
    private void UpdateClientHint()
    {
        string? text = null;
        if (SelectedComponent is { } id)
        {
            var overriding = _clients.ClientsOverriding(id);
            string machineClient = _machine.Client;
            if (machineClient.Length > 0 && _clients.Overrides(machineClient, id))
                text = $"Client {_clients.ExactId(machineClient)}: {id} uses the {_clients.ExactId(machineClient)} branch build.";
            else if (machineClient.Length > 0)
                text = $"Client {_clients.ExactId(machineClient)}: {id} has no {_clients.ExactId(machineClient)} branch, so the default build is used.";
            else if (overriding.Count > 0)
                text = $"No Client is set, so the default build is used. {id} also has branch builds for {string.Join(", ", overriding)}; set the Client on the Dashboard to deploy one.";
        }
        ClientHint.Text = text ?? "";
        ClientHint.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // Target directory dan service name satu sumber: Component release. Tidak ada yang bisa diketik.
    private void ShowTarget()
    {
        ComponentRelease? release = null;
        string? releaseProblem = null;
        if (SelectedComponent is { } id && SelectedVersion is { } version)
        {
            (release, var errors) = _components.Load(id, version);
            if (release is null)
                releaseProblem = $"{id}@{version} is not saved as a component release: {errors.FirstOrDefault()}";
        }
        UpdateBuildHint(releaseProblem);
        TargetBox.Text = release?.TargetDirectory ?? "";
        TargetBox.ToolTip = string.IsNullOrEmpty(TargetBox.Text) ? null : TargetBox.Text;
        ServiceBox.Text = release is null ? "" : release.ServiceName.Length > 0 ? release.ServiceName : "(no service)";
    }

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        try { LoadComponents(previous: null); LoadBuilds(previous: null); ShowTarget(); }
        finally { _updating = false; }
        ClearPreview();
    }

    private void OnComponentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        try { LoadBuilds(previous: null); ShowTarget(); }
        finally { _updating = false; }
        ClearPreview();
    }

    private void OnBuildChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        ShowTarget();
        ClearPreview();
    }

    private void OnOperationChanged(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        ClearPreview();
    }

    /// <summary>
    /// Dipakai tombol "Use in Developer Console": memilih profile yang memuat component itu (yang terdeteksi bila cocok,
    /// kalau tidak profile pertama), lalu component dan build, dan menghapus preview.
    /// </summary>
    public void Preselect(string componentId, string releaseVersion)
    {
        if (_locked || IsBusy)
            return;

        var candidates = new List<string>();
        foreach (string id in ProfileBox.Items.OfType<string>())
            if (_profiles.Load(id).Profile?.Components.Any(c => c.Id == componentId) == true)
                candidates.Add(id);

        string? profile = _detectedProfile is not null && candidates.Contains(_detectedProfile) ? _detectedProfile
            : SelectedProfile is not null && candidates.Contains(SelectedProfile) ? SelectedProfile
            : candidates.FirstOrDefault();
        if (profile is null)
        {
            Message?.Invoke($"No profile contains the component '{componentId}'. Add it to a profile first.");
            return;
        }

        _updating = true;
        try
        {
            ProfileBox.SelectedItem = profile;
            LoadComponents(previous: componentId);
            LoadBuilds(previous: releaseVersion);
            ShowTarget();
        }
        finally { _updating = false; }
        ClearPreview();

        if (SelectedVersion != releaseVersion)
            Message?.Invoke($"The build {componentId}@{releaseVersion} is not imported yet. Import it in Components first.");
    }

    // ---------- Preview ----------

    private bool HasSelection => SelectedProfile is not null && SelectedComponent is not null && SelectedVersion is not null;
    private bool HasOperation => DeployBox.IsChecked == true || ConfigBox.IsChecked == true || RestartBox.IsChecked == true;
    private bool CanPreview => !_locked && !_recoveryNeeded && !IsBusy && HasSelection && HasOperation;

    private void ClearPreview()
    {
        _plan = null;
        _errors = [];
        _result = null;
        ShowPreviewState();
    }

    /// <summary>Menyusun plan dari pilihan saat ini (sama dengan menekan Preview plan). True bila plan valid.</summary>
    public bool PreviewPlan()
    {
        if (!CanPreview)
            return false;

        _result = null;
        var request = new PlanRequest(SelectedProfile!, SelectedComponent!, SelectedVersion!,
            DeployBox.IsChecked == true, ConfigBox.IsChecked == true, RestartBox.IsChecked == true,
            SnapshotFolder: SnapshotBox.IsChecked == true);

        var previous = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;   // Preview membaca dan menghitung hash file; bisa beberapa detik
        try
        {
            var (plan, errors) = _planner.Build(request);
            _plan = plan;
            _errors = errors;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _plan = null;
            _errors = [$"The plan could not be prepared: {ex.Message}"];
        }
        finally
        {
            Mouse.OverrideCursor = previous;
        }

        ShowPreviewState();
        return _plan is not null;
    }

    private void OnPreviewClick(object sender, RoutedEventArgs e) => PreviewPlan();

    private void ShowPreviewState()
    {
        PreviewEmpty.Visibility = Visibility.Collapsed;
        PreviewErrors.Visibility = Visibility.Collapsed;
        PlanPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;

        if (_result is not null)
        {
            ShowResult(_result);
            ResultPanel.Visibility = Visibility.Visible;
        }
        else if (_errors.Count > 0)
        {
            ErrorLines.Children.Clear();
            foreach (string error in _errors)
                ErrorLines.Children.Add(new TextBlock { Text = "• " + error, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
            PreviewErrors.Visibility = Visibility.Visible;
        }
        else if (_plan is not null)
        {
            ShowPlan(_plan);
            PlanPanel.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewEmpty.Visibility = Visibility.Visible;
        }

        UpdateActionBar();
    }

    private void ShowPlan(DeploymentPlan plan)
    {
        PlanRows.Children.Clear();

        if (plan.DeployFiles)
        {
            int replace = plan.Files.Count(f => f.Change == FileChange.Replace);
            int added = plan.Files.Count(f => f.Change == FileChange.New);
            int same = plan.Files.Count(f => f.Change == FileChange.Same);
            AddPlanRow("Files", $"{replace} replace, {added} new, {same} same");
        }

        if (plan.ServiceName.Length > 0 && (plan.DeployFiles || plan.RestartService))
        {
            var steps = new List<string>();
            if (plan.StopServiceBefore) steps.Add("Stop");
            if (plan.StartServiceAfter) steps.Add("Start");
            AddPlanRow("Service", $"{plan.ServiceName} is {plan.ServiceStateAtPlan}: " +
                (steps.Count > 0 ? string.Join(" → ", steps) : "no stop or start needed") +
                (plan.DeployFiles && plan.ServiceExecutable.Length == 0 ? "\nWarning: the service's executable path could not be read, so it was not checked against the target directory." : ""));
        }

        if (plan.ServiceName.Length == 0 && (plan.DeployFiles || plan.RestartService))
        {
            AddPlanRow("Program", plan.RestartsPrograms
                ? $"{string.Join(", ", plan.ProgramsAtPlan.Select(System.IO.Path.GetFileName))} running: Stop → Start"
                : "no program running from the target directory: nothing to stop or start");
        }

        if (plan.ApplyConfig)
        {
            var lines = plan.RegistryChanges.Select(r => $"Registry · {r.ValueName}: {r.CurrentValue ?? "(not set)"} → {r.NewValue}")
                .Concat(plan.IniChanges.Select(i => $"INI · [{i.Section}] {i.Key}: {i.CurrentValue ?? "(not set)"} → {i.NewValue}"));
            AddPlanRow("Config", string.Join("\n", lines));
        }

        AddPlanRow("Backup folder", Paths.BackupDir(_machine.LocalCacheDirectory, plan.ExecId));
        if (plan.SnapshotFolder)
            AddPlanRow("Snapshot", $"Whole folder {plan.TargetDirectory} ({FormatSize(plan.SnapshotBytes)}) is copied to the backup folder first");

        var guards = new List<string> { "Environment", "Paths", "Allow-list" };
        if (plan.DeployFiles) { guards.Add("Build hash"); guards.Add("Disk space"); }
        guards.Add("No pending backup");
        AddPlanRow("Guards", string.Join("   ", guards.Select(g => "✔ " + g)));

        FileList.ItemsSource = plan.Files.Select(f => new FileRow(
            f.Change.ToString(), f.RelativePath, FormatSize(f.Size),
            (Brush)FindResource(f.Change switch { FileChange.Replace => "Brush.Warning.Fg", FileChange.New => "Brush.Success.Fg", _ => "Brush.Muted" }))).ToList();
        FileListExpander.Visibility = plan.DeployFiles && plan.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FileListExpander.IsExpanded = false;
    }

    private void AddPlanRow(string label, string value)
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, ToolTip = value };
        PlanRows.Children.Add(new HeaderedContentControl { Style = (Style)FindResource("KeyValueRow"), Header = label, Content = text });
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.#} MB" : bytes >= 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes} B";

    // ---------- Hasil eksekusi ----------

    private void ShowResult(DeploymentResult result)
    {
        ResultLines.Children.Clear();
        var (kind, title) = result.Outcome switch
        {
            HistoryOutcome.Succeeded => (StatusKind.Success, "Execution succeeded"),
            HistoryOutcome.FailedRestored => (StatusKind.Error, "Execution failed. The machine was restored from the backup automatically."),
            HistoryOutcome.FailedRestoreIncomplete => (StatusKind.Error, "Execution failed and the automatic restore is INCOMPLETE."),
            HistoryOutcome.Rejected => (StatusKind.Warning, "Execution rejected. Nothing was changed on this machine."),
            _ => (StatusKind.Neutral, result.Outcome.ToString()),
        };
        ResultNotice.Kind = kind;
        AddResultLine(title, bold: true);

        if (result.Succeeded)
        {
            AddResultLine($"Backup kept at {result.BackupDirectory}");
            if (Directory.Exists(Paths.SnapshotDir(result.BackupDirectory)))
                AddResultLine($"Whole-folder snapshot kept at {Paths.SnapshotDir(result.BackupDirectory)}");
        }
        foreach (string error in result.Errors)
            AddResultLine("• " + error);        if (result.RestoreFailures.Count > 0)
        {
            AddResultLine("These parts could not be restored:", bold: true, top: 6);
            foreach (string failure in result.RestoreFailures)
                AddResultLine("• " + failure);
            AddResultLine("Open the Dashboard and press Restore before continuing.", bold: true, top: 6);
        }
        foreach (string problem in result.HistoryProblems)
            AddResultLine("History: " + problem, top: 6);

        bool hasBackup = result.BackupDirectory.Length > 0;
        OpenBackupButton.Visibility = hasBackup ? Visibility.Visible : Visibility.Collapsed;
        OpenBackupButton.IsEnabled = hasBackup && Directory.Exists(result.BackupDirectory);
        OpenBackupButton.Tag = result.BackupDirectory;
    }

    private void AddResultLine(string text, bool bold = false, double top = 1) =>
        ResultLines.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 1),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        });

    private void OnOpenBackupClick(object sender, RoutedEventArgs e)
    {
        string dir = OpenBackupButton.Tag as string ?? "";
        if (!Directory.Exists(dir))
        {
            Message?.Invoke("Backup folder was not found.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Message?.Invoke($"Cannot open backup folder: {ex.Message}");
        }
    }

    // ---------- Action bar ----------

    private void UpdateActionBar()
    {
        PreviewButton.IsEnabled = CanPreview;
        ExecuteButton.IsEnabled = _plan is not null && !_locked && !_recoveryNeeded && !IsBusy;

        string status = IsBusy ? "Executing… keep ION PackMan open until it finishes."
            : _recoveryNeeded ? "Recovery needed: restore the backup from the Dashboard before continuing."
            : SelectedProfile is null ? "Select a profile"
            : SelectedComponent is null ? "Select a component"
            : SelectedVersion is null ? "Select an imported build"
            : !HasOperation ? "Select at least one operation"
            : _plan is not null ? $"Ready. Execute will change {_machine.MachineName}."
            : _errors.Count > 0 ? "Fix the problems above"
            : _result is not null ? "Finished. Preview the plan again to run another execution."
            : "Preview required";
        StatusText.Text = status;
        StatusText.ToolTip = status;
        StatusText.Foreground = (Brush)FindResource(_recoveryNeeded || (_errors.Count > 0 && _plan is null) ? "Brush.Error.Fg"
            : _plan is not null ? "Brush.Success.Fg" : "Brush.Muted");
    }

    // ---------- Execute ----------

    private async void OnExecuteClick(object sender, RoutedEventArgs e)
    {
        if (_plan is null || IsBusy)
            return;

        var owner = Window.GetWindow(this);
        string question = $"Execute on {_machine.MachineName} (Development)? A backup is created first.";
        var answer = owner is null
            ? MessageBox.Show(question, "Execute", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel)
            : MessageBox.Show(owner, question, "Execute", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        var dialog = new ProgressDialog(_plan, _machine.MachineName);
        if (owner is not null)
            dialog.Owner = owner;

        // Progress<T> dibuat di thread UI, jadi Report dikirim kembali ke thread UI (dialog juga menjaga ini sendiri).
        var task = ExecutePlanAsync(new Progress<DeployStep>(dialog.Report));
        _ = task.ContinueWith(t => dialog.Complete(t.IsCompletedSuccessfully && t.Result is { } r ? r : Aborted()),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());

        dialog.ShowDialog();   // modal: pengguna baru bisa menutupnya setelah Complete
        await task;
    }

    private DeploymentResult Aborted() =>
        new(HistoryOutcome.Rejected, _plan?.ExecId ?? "", ["The execution did not start."], [], "", []);

    /// <summary>
    /// Menjalankan plan terakhir yang valid di thread latar (UI tidak membeku), lalu menampilkan hasilnya di card Execution Preview.
    /// Null bila tidak ada plan valid. Dipakai tombol Execute (setelah konfirmasi) dan tes.
    /// </summary>
    public async Task<DeploymentResult?> ExecutePlanAsync(IProgress<DeployStep>? progress = null)
    {
        if (_plan is null || _locked || _recoveryNeeded || IsBusy)
            return null;
        if (!_gate.TryEnter())
        {
            _plan = null;
            _errors = ["Another execution (a profile switch) is running. Wait until it finishes, then preview again."];
            ShowPreviewState();
            return null;
        }

        var plan = _plan;
        IsBusy = true;
        FormPanel.IsEnabled = false;
        UpdateActionBar();

        DeploymentResult result;
        try
        {
            result = await Task.Run(() => _deployer.Run(plan, progress));
        }
        catch (Exception ex)
        {
            // Deployer menangkap kegagalan setelah backup dibuat; ini hanya untuk kejadian tak terduga sebelum itu.
            result = new DeploymentResult(HistoryOutcome.Rejected, plan.ExecId, [$"Unexpected error: {ex.Message}"], [], "", []);
        }

        _gate.Exit();
        IsBusy = false;
        FormPanel.IsEnabled = true;
        _plan = null;       // satu plan, satu eksekusi: untuk menjalankan lagi, Preview ulang
        _errors = [];
        _result = result;

        // Bila restore tidak lengkap atau ada backup yang tersisa, konsol harus menolak apa pun sampai dipulihkan.
        try
        {
            _recoveryNeeded = result.Outcome == HistoryOutcome.FailedRestoreIncomplete || AppServices.PendingBackupLines(_backups).Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _recoveryNeeded = true;   // tidak bisa memastikan backup bersih: lebih aman mengunci
        }

        ShowPreviewState();
        Message?.Invoke(result.Succeeded ? "Execution succeeded."
            : result.Outcome == HistoryOutcome.Rejected ? "Execution rejected. Nothing was changed."
            : result.Outcome == HistoryOutcome.FailedRestored ? "Execution failed. Restored from backup."
            : "Execution failed. Restore needed.");
        return result;
    }

    // ---------- Action bar lengket ----------

    // MainWindow menaruh layar di dalam ScrollViewer (tinggi tak terbatas). Agar action bar tetap di bawah area konten,
    // layar mengambil tinggi viewport ScrollViewer itu; isi di atasnya scroll sendiri. Di luar MainWindow (tes) tinggi mengikuti isi.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostScroll = FindAncestor<ScrollViewer>(this);
        if (_hostScroll is null)
            return;
        _hostScroll.SizeChanged += OnHostSizeChanged;
        FitToViewport();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostScroll is not null)
            _hostScroll.SizeChanged -= OnHostSizeChanged;
        _hostScroll = null;
        ClearValue(HeightProperty);
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => FitToViewport();

    private void FitToViewport()
    {
        if (_hostScroll is null)
            return;
        var margin = (_hostScroll.Content as FrameworkElement)?.Margin ?? new Thickness();
        Height = Math.Max(MinScreenHeight, _hostScroll.ActualHeight - margin.Top - margin.Bottom);
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var node = VisualTreeHelper.GetParent(start); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is T found)
                return found;
        return null;
    }
}
