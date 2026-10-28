using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IonPackMan.Deployment;
using IonPackMan.History;

namespace IonPackMan.Screens;

public enum StepState { Pending, Running, Done, Failed }

/// <summary>
/// Dialog progres modal 480x320 untuk eksekusi (T28). Menampilkan hanya langkah yang berlaku bagi plan, dengan tanda centang
/// untuk yang selesai dan penanda langkah yang sedang berjalan. Tidak bisa ditutup selama berjalan (tombol X, Alt+F4);
/// tombol Close baru muncul setelah <see cref="Complete"/>. <see cref="Report"/> aman dipanggil dari thread mana pun.
/// </summary>
public partial class ProgressDialog : Window
{
    private sealed class StepRow
    {
        public required DeployStep Step;
        public required string Label;
        public required TextBlock Marker;
        public required TextBlock Text;
        public StepState State = StepState.Pending;
    }

    private readonly List<StepRow> _rows = [];

    public ProgressDialog(DeploymentPlan plan, string? machineName = null)
    {
        InitializeComponent();
        HeaderText.Text = (machineName is null ? "Executing " : $"Executing on {machineName}: ") + $"{plan.ComponentId} {plan.ReleaseVersion}";

        Add(DeployStep.Checking, "Checking safety guards");
        Add(DeployStep.BackingUp, "Creating backup");
        if (plan.StopServiceBefore) Add(DeployStep.StoppingService, $"Stopping service {plan.ServiceName}");
        if (plan.DeployFiles) Add(DeployStep.CopyingFiles, "Copying files");
        if (plan.ApplyConfig) Add(DeployStep.ApplyingConfig, "Applying configuration");
        if (plan.StartServiceAfter) Add(DeployStep.StartingService, $"Starting service {plan.ServiceName}");
        Add(DeployStep.Finalizing, "Finalizing");

        IsRunning = true;
        Report(DeployStep.Checking);
    }

    /// <summary>True sampai <see cref="Complete"/> dipanggil; selama itu dialog menolak ditutup.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Langkah yang tampil beserta statusnya.</summary>
    public IReadOnlyList<(DeployStep Step, string Label, StepState State)> Steps =>
        _rows.Select(r => (r.Step, r.Label, r.State)).ToList();

    public string Summary => SummaryText.Text;
    public bool IsCloseButtonVisible => CloseButton.Visibility == Visibility.Visible;

    private void Add(DeployStep step, string label)
    {
        var marker = new TextBlock { Width = 22, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var row = new StepRow { Step = step, Label = label, Marker = marker, Text = text };
        StepsPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Height = 21, Children = { marker, text } });
        _rows.Add(row);
        Paint(row);
    }

    /// <summary>Menandai langkah baru sebagai berjalan dan semua langkah sebelumnya selesai. Aman dari thread mana pun.</summary>
    public void Report(DeployStep step)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Report(step));
            return;
        }
        if (!IsRunning)
            return;   // laporan terlambat setelah selesai tidak boleh menimpa hasil

        if (step == DeployStep.Restoring)
        {
            // Eksekusi gagal: langkah yang berjalan ditandai gagal, lalu pemulihan otomatis dimulai.
            foreach (var r in _rows.Where(r => r.State == StepState.Running))
                Set(r, StepState.Failed);
            SummaryText.Text = "Restoring from backup…";
            SummaryText.Foreground = (Brush)FindResource("Brush.Warning.Fg");
            return;
        }

        int index = _rows.FindIndex(r => r.Step == step);
        if (index < 0)
            return;
        for (int i = 0; i < _rows.Count; i++)
            Set(_rows[i], i < index ? StepState.Done : i == index ? StepState.Running : StepState.Pending);
    }

    /// <summary>Eksekusi selesai (apa pun hasilnya): tampilkan ringkasan dan tombol Close.</summary>
    public void Complete(DeploymentResult result)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Complete(result));
            return;
        }

        IsRunning = false;
        if (result.Succeeded)
        {
            foreach (var r in _rows) Set(r, StepState.Done);
        }
        else
        {
            // Langkah yang sedang berjalan saat gagal ditandai gagal; sisanya tidak pernah dijalankan.
            foreach (var r in _rows.Where(r => r.State == StepState.Running)) Set(r, StepState.Failed);
        }

        (string text, string brushKey) = result.Outcome switch
        {
            HistoryOutcome.Succeeded => ("Completed successfully. A backup was kept.", "Brush.Success.Fg"),
            HistoryOutcome.FailedRestored => ("Failed. The machine was restored from the backup.", "Brush.Error.Fg"),
            HistoryOutcome.FailedRestoreIncomplete => ("Failed, and the automatic restore is incomplete. Restore from the Dashboard.", "Brush.Error.Fg"),
            HistoryOutcome.Rejected => ("Rejected. Nothing was changed on this machine.", "Brush.Warning.Fg"),
            _ => (result.Outcome.ToString(), "Brush.Text"),
        };
        SummaryText.Text = text;
        SummaryText.Foreground = (Brush)FindResource(brushKey);
        SubText.Text = "Details are shown in the Execution Preview card.";
        Title = result.Succeeded ? "Execution finished" : "Execution failed";
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.Focus();
    }

    private void Set(StepRow row, StepState state)
    {
        row.State = state;
        Paint(row);
    }

    private void Paint(StepRow row)
    {
        (string glyph, string brush) = row.State switch
        {
            StepState.Done => ("✔", "Brush.Success.Fg"),
            StepState.Running => ("▶", "Brush.Blue"),
            StepState.Failed => ("✖", "Brush.Red"),
            _ => ("○", "Brush.Muted"),
        };
        row.Marker.Text = glyph;
        row.Marker.Foreground = (Brush)FindResource(brush);
        row.Text.Foreground = (Brush)FindResource(row.State == StepState.Pending ? "Brush.Muted" : "Brush.Text");
        row.Text.FontWeight = row.State == StepState.Running ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (IsRunning)
            e.Cancel = true;   // X dan Alt+F4: menutup di tengah eksekusi tidak boleh
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
