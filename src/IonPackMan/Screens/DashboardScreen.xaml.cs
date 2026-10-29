using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IonPackMan.Controls;
using IonPackMan.Deployment;
using IonPackMan.Machine;

namespace IonPackMan.Screens;

/// <summary>Dashboard (PRD R1–R2): kondisi mesin, service, dan proses ION. Hanya membaca.</summary>
public partial class DashboardScreen : UserControl, IScreen
{
    private readonly DashboardReader _reader;
    private readonly RecoveryService? _recovery;
    private DashboardSnapshot? _snapshot;

    public event Action<string>? Message;

    /// <param name="recovery">null bila machine.ini/allowlist.ini tidak valid (tidak ada yang bisa dipulihkan atau dijalankan).</param>
    public DashboardScreen(DashboardReader reader, RecoveryService? recovery = null)
    {
        InitializeComponent();
        _reader = reader;
        _recovery = recovery;
    }

    /// <summary>Membaca ulang kondisi mesin (saat layar dibuka dan saat Refresh ditekan) tanpa memblokir UI.</summary>
    public void Refresh() => _ = RefreshAsync();

    private Task? _loading;
    private bool _readAgain;

    /// <summary>
    /// Pembacaan mesin lambat (enumerasi proses dan service), jadi berjalan di thread latar; data terakhir tetap tampil
    /// dengan penanda "Reading machine status…". Permintaan yang datang saat pembacaan berjalan digabung: satu pembacaan
    /// lagi setelah yang berjalan selesai, bukan satu per permintaan. Banner pemulihan (murah) langsung diperbarui.
    /// </summary>
    public Task RefreshAsync()
    {
        ShowRecovery();
        if (_loading is not null)
        {
            _readAgain = true;
            return _loading;
        }
        return _loading = LoadAsync();
    }

    private async Task LoadAsync()
    {
        LoadingText.Visibility = Visibility.Visible;
        try
        {
            do
            {
                _readAgain = false;
                try
                {
                    _snapshot = await Task.Run(_reader.Read);
                    Show(_snapshot);
                }
                catch (Exception ex)
                {
                    Message?.Invoke($"Could not read the machine status: {ex.Message}");
                }
            }
            while (_readAgain);
        }
        finally
        {
            _loading = null;
            LoadingText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Banner merah + tombol Restore bila ada backup yang belum selesai atau tidak terbaca.</summary>
    private void ShowRecovery()
    {
        var (pending, problems) = _recovery?.FindPending() ?? ([], []);
        RecoveryBanner.Visibility = pending.Count + problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.IsEnabled = pending.Count > 0;

        var lines = pending.Select(p => $"{p.Component} ({p.ExecId}): {p.Status}").Concat(problems).ToList();
        RecoveryText.Text = "An earlier execution was interrupted and the machine may not be back in its original state. " +
                            "Restore before using the Developer Console." + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    /// <summary>Memulihkan semua backup tertunda (terlama dulu). Dipakai tombol Restore dan tes; dialog konfirmasi ada di handler tombol.</summary>
    public List<string> RestorePending()
    {
        var failures = new List<string>();
        if (_recovery is null)
            return failures;

        foreach (var pending in _recovery.FindPending().Pending)
        {
            var result = _recovery.Restore(pending.ExecId);
            failures.AddRange(result.Failures.Select(f => $"{pending.ExecId}: {f}"));
            failures.AddRange(result.HistoryProblems.Select(f => $"History: {f}"));
        }

        Refresh();
        Message?.Invoke(failures.Count == 0 ? "Backup restored. The Developer Console is available again." : "Restore is not complete. See the Dashboard for details.");
        return failures;
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show("Restore the unfinished backup now? Files, Registry/INI values and the service state are returned to what they were before the interrupted execution.",
            "Restore", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
            return;

        var previous = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        List<string> failures;
        try
        {
            failures = RestorePending();
        }
        finally
        {
            Mouse.OverrideCursor = previous;
        }

        if (failures.Count > 0)
            MessageBox.Show(string.Join(Environment.NewLine, failures), "Restore is not complete", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Show(DashboardSnapshot s)
    {
        MachineName.Text = s.MachineName;
        EnvironmentText.Text = s.Environment;
        ProfileName.Text = s.ProfileText;
        ProfilePill.Text = s.ProfileKind == StatusKind.Success ? "Detected" : "Not detected";
        ProfilePill.Kind = s.ProfileKind;
        ProfilePill.ToolTip = s.ProfileReason;
        WindowsText.Text = s.WindowsVersion;
        DeploymentPill.Text = s.DeploymentState;
        DeploymentPill.Kind = s.DeploymentKind;
        BaseDirectory.Text = s.BaseDirectory;
        BaseDirectory.ToolTip = s.BaseDirectory;
        ProfileConfig.Text = s.ProfileConfig;
        ProfileConfig.ToolTip = s.ProfileConfig;
        LastDeployment.Text = s.LastDeployment;

        // Alasan "Unknown" harus terlihat (R1), bukan hanya di tooltip.
        ProblemNotice.Visibility = s.ProfileKind == StatusKind.Success ? Visibility.Collapsed : Visibility.Visible;
        ProblemNotice.Content = new TextBlock { Text = $"Profile not detected: {s.ProfileReason}", TextWrapping = TextWrapping.Wrap };

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_snapshot is null)
            return;

        string q = SearchBox.Text.Trim();
        bool Match(params string[] parts) => q.Length == 0 || parts.Any(p => p.Contains(q, StringComparison.OrdinalIgnoreCase));

        var services = _snapshot.Services.Where(r => Match(r.Service, r.DisplayName, r.Component, r.Status, r.StartType)).ToList();
        ServicesGrid.ItemsSource = services;
        ServicesGrid.Visibility = services.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ServicesEmpty.Visibility = services.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ProcessesGrid.ItemsSource = _snapshot.Processes.Where(r => Match(r.Process, r.Component, r.Status, r.Pid)).ToList();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        Message?.Invoke("Machine status refreshed.");
    }
}
