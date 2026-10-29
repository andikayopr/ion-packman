using System.IO;
using System.Windows;
using System.Windows.Controls;
using IonPackMan.Controls;
using IonPackMan.Deployment;
using IonPackMan.History;

namespace IonPackMan.Screens;

/// <summary>
/// Dialog pindah Profile (dibuka dari layar Profiles): menyusun rencana untuk seluruh Profile saat dibuka, menampilkannya,
/// lalu menjalankannya setelah pengguna menekan Switch. Aturannya ada di <see cref="ProfileSwitcher"/>; dialog hanya
/// menampilkan dan mengunci alur. Tidak modal (dibuka dengan Show): jendela utama tetap bisa dipakai, mis. membuka Dashboard,
/// selama switch berjalan. Tidak bisa ditutup selama berjalan.
/// </summary>
public partial class SwitchProfileDialog : Window
{
    private readonly ProfileSwitcher _switcher;
    private SwitchPlan? _plan;

    public SwitchProfileDialog(ProfileSwitcher switcher, string profileId, string machineName)
    {
        InitializeComponent();
        _switcher = switcher;
        ProfileId = profileId;
        HeaderText.Text = $"Switch {machineName} to {profileId}";
        BuildPlan();
    }

    public string ProfileId { get; }

    /// <summary>Dipanggil setelah eksekusi selesai (apa pun hasilnya), dari thread UI. Dialog tidak modal: pengguna bisa sedang di layar lain.</summary>
    public event Action<DeploymentResult>? Completed;

    /// <summary>Rencana yang valid; null bila ditolak (lihat <see cref="Errors"/>).</summary>
    public SwitchPlan? Plan => _plan;

    public List<string> Errors { get; private set; } = [];

    public bool IsRunning { get; private set; }

    /// <summary>Hasil eksekusi; null sebelum Switch dijalankan.</summary>
    public DeploymentResult? Result { get; private set; }

    /// <summary>True bila semua langkah berhasil dan Profile sudah ditandai aktif.</summary>
    public bool Switched => Result is { Succeeded: true } r && (r.Warnings?.Count ?? 0) == 0;

    public bool CanSwitch => _plan is not null && !IsRunning && Result is null;

    private void BuildPlan()
    {
        var previous = System.Windows.Input.Mouse.OverrideCursor;
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;   // membaca dan menghitung hash; bisa beberapa detik
        try
        {
            var (plan, errors) = _switcher.Build(ProfileId);
            _plan = plan;
            Errors = errors;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _plan = null;
            Errors = [$"The plan could not be prepared: {ex.Message}"];
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = previous;
        }

        ErrorLines.Children.Clear();
        PlanRows.Children.Clear();
        if (_plan is null)
        {
            foreach (string error in Errors)
                ErrorLines.Children.Add(new TextBlock { Text = "• " + error, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
            ErrorNotice.Visibility = Visibility.Visible;
            StatusText.Text = "Nothing was changed. Fix the problems above.";
        }
        else
        {
            ShowPlan(_plan);
            StatusText.Text = "Review the plan, then press Switch.";
        }
        UpdateButtons();
    }

    // Satu baris per component, urut sesuai eksekusi, lalu langkah akhir: hentikan service Profile lama, tandai Profile aktif.
    private void ShowPlan(SwitchPlan plan)
    {
        AddRow("Switch", $"{plan.PreviousProfileId ?? "(unknown)"} → {plan.ProfileId}");

        foreach (var step in plan.Steps)
        {
            int replace = step.Files.Count(f => f.Change == FileChange.Replace);
            int added = step.Files.Count(f => f.Change == FileChange.New);
            int same = step.Files.Count(f => f.Change == FileChange.Same);
            var lines = new List<string> { $"deploy {step.ReleaseVersion}: {replace} replace, {added} new, {same} same" };
            if (step.ApplyConfig) lines.Add($"apply config ({step.RegistryChanges.Count} Registry, {step.IniChanges.Count} INI)");
            if (step.ServiceName.Length > 0)
                lines.Add(step.StartServiceAfter ? $"{(step.StopServiceBefore ? "restart" : "start")} {step.ServiceName}" : $"{step.ServiceName} not installed yet");
            AddRow(step.ComponentId, string.Join("\n", lines));
        }

        AddRow("Stop", plan.ServicesToStop.Count > 0
            ? string.Join("\n", plan.ServicesToStop.Select(s => $"{s.ServiceName} ({s.ComponentId}, not used by {plan.ProfileId})"))
            : plan.PreviousProfileId is null ? "Previous profile unknown: no service is stopped" : "No service of the previous profile needs to stop");
        AddRow("Activate", $"Writes Profile={plan.ProfileId} to machine.ini after everything above succeeds");
    }

    private void AddRow(string label, string value)
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, ToolTip = value };
        PlanRows.Children.Add(new HeaderedContentControl { Style = (Style)FindResource("KeyValueRow"), Header = label, Content = text });
    }

    /// <summary>Menjalankan rencana di thread latar (UI tidak membeku). Null bila tidak ada rencana valid atau sudah berjalan.</summary>
    public async Task<DeploymentResult?> ExecuteAsync()
    {
        if (!CanSwitch)
            return null;

        var plan = _plan!;
        IsRunning = true;
        StatusText.Text = "Switching… do not close ION PackMan until this finishes.";
        UpdateButtons();

        DeploymentResult result;
        try
        {
            result = await Task.Run(() => _switcher.Run(plan));
        }
        catch (Exception ex)
        {
            result = new DeploymentResult(HistoryOutcome.Rejected, "", [$"Unexpected error: {ex.Message}"], [], "", []);
        }

        IsRunning = false;
        Result = result;
        ShowResult(result);
        UpdateButtons();
        Completed?.Invoke(result);
        return result;
    }

    private void ShowResult(DeploymentResult result)
    {
        PlanRows.Children.Clear();
        ResultLines.Children.Clear();

        bool activated = Switched;
        var (kind, title) = activated ? (StatusKind.Success, $"Switched. Profile {ProfileId} is now active.")
            : result.Succeeded ? (StatusKind.Warning, "Components were deployed, but the switch is not complete.")
            : result.Outcome == HistoryOutcome.Rejected ? (StatusKind.Warning, "Rejected. Nothing was changed on this machine.")
            : (StatusKind.Error, "The switch failed. The failing component was restored from its backup; earlier components stay deployed.");
        ResultNotice.Kind = kind;
        AddResultLine(title, bold: true);
        foreach (string error in result.Errors)
            AddResultLine("• " + error);
        foreach (string warning in result.Warnings ?? [])
            AddResultLine("⚠ " + warning, bold: true);
        if (result.RestoreFailures.Count > 0)
        {
            AddResultLine("These parts could not be restored:", bold: true);
            foreach (string failure in result.RestoreFailures)
                AddResultLine("• " + failure);
            AddResultLine("Open the Dashboard and press Restore before continuing.", bold: true);
        }
        foreach (string problem in result.HistoryProblems)
            AddResultLine("History: " + problem);

        ResultNotice.Visibility = Visibility.Visible;
        StatusText.Text = activated ? "Done." : "Not activated.";
        Title = activated ? "Profile switched" : "Switch not completed";
    }

    private void AddResultLine(string text, bool bold = false) =>
        ResultLines.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        });

    private void UpdateButtons()
    {
        bool finished = Result is not null;
        SwitchButton.Visibility = finished ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = finished ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Visibility = finished ? Visibility.Visible : Visibility.Collapsed;
        SwitchButton.IsEnabled = CanSwitch;
        CancelButton.IsEnabled = !IsRunning;
    }

    private async void OnSwitchClick(object sender, RoutedEventArgs e) => await ExecuteAsync();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (IsRunning)
            e.Cancel = true;   // X dan Alt+F4: menutup di tengah eksekusi tidak boleh
    }
}
