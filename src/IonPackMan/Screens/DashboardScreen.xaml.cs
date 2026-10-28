using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IonPackMan.Controls;
using IonPackMan.Machine;

namespace IonPackMan.Screens;

/// <summary>Dashboard (PRD R1–R2): kondisi mesin, service, dan proses ION. Hanya membaca.</summary>
public partial class DashboardScreen : UserControl, IScreen
{
    private readonly DashboardReader _reader;
    private DashboardSnapshot? _snapshot;

    public event Action<string>? Message;

    public DashboardScreen(DashboardReader reader)
    {
        InitializeComponent();
        _reader = reader;
    }

    /// <summary>Membaca ulang kondisi mesin (saat layar dibuka dan saat Refresh ditekan).</summary>
    public void Refresh()
    {
        var previous = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            _snapshot = _reader.Read();
        }
        finally
        {
            Mouse.OverrideCursor = previous;
        }
        Show(_snapshot);
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

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        Refresh();
        Message?.Invoke("Machine status refreshed.");
    }
}
