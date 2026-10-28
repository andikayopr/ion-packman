using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using IonPackMan.Controls;
using IonPackMan.History;

namespace IonPackMan.Screens;

/// <summary>Daftar History (R12). Hanya membaca lewat HistoryStore dan mengisi kontrol; tidak ada ubah/hapus.</summary>
public partial class HistoryScreen : UserControl, IScreen
{
    public const int PageSize = 8;

    /// <summary>Satu baris tabel (data untuk binding).</summary>
    public sealed record Row(HistoryRecord Record)
    {
        public string Time => Record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss");
        public string Component => Record.Component;
        public string Build => Record.BuildVersion.Length > 0 ? Record.BuildVersion : "—";
        public string OutcomeText => Record.Outcome.ToString();
        public StatusKind Kind => KindOf(Record.Outcome);
    }

    private readonly HistoryStore _store;
    private List<HistoryRecord> _all = [];
    private List<HistoryRecord> _filtered = [];
    private int _page;

    /// <summary>Pesan singkat untuk Toast di MainWindow.</summary>
    public event Action<string>? Message;

    public HistoryScreen(HistoryStore store)
    {
        _store = store;
        InitializeComponent();
        Refresh();
    }

    public static StatusKind KindOf(HistoryOutcome outcome) => outcome switch
    {
        HistoryOutcome.Succeeded => StatusKind.Success,
        HistoryOutcome.Rejected => StatusKind.Warning,
        HistoryOutcome.FailedRestored => StatusKind.Error,
        HistoryOutcome.FailedRestoreIncomplete => StatusKind.Error,
        HistoryOutcome.RestoredAfterCrash => StatusKind.Info,
        _ => StatusKind.Neutral,
    };

    /// <summary>Membaca ulang folder History. Pencarian dan halaman saat ini dipertahankan.</summary>
    public void Refresh()
    {
        string? selectedId = (Table.SelectedItem as Row)?.Record.Id;
        var (records, problems) = _store.GetAll();
        _all = records;

        ProblemsNotice.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProblemsText.Text = problems.Count == 0 ? "" :
            $"{problems.Count} history problem(s) found. The files below were skipped:\n" + string.Join("\n", problems);

        ApplyFilter(resetPage: false);
        if (selectedId is not null)
            Table.SelectedItem = (Table.ItemsSource as IEnumerable<Row>)?.FirstOrDefault(r => r.Record.Id == selectedId);
    }

    // ---------- Tabel, cari, paging ----------

    private void ApplyFilter(bool resetPage)
    {
        string query = SearchBox.Text.Trim();
        _filtered = query.Length == 0 ? _all : _all.Where(r => Matches(r, query)).ToList();
        if (resetPage)
            _page = 0;
        ShowPage();
    }

    private static bool Matches(HistoryRecord r, string query) =>
        new[] { new Row(r).Time, r.Component, r.BuildVersion, r.Outcome.ToString(), r.Operator, r.Profile, r.Machine, r.Id }
            .Any(text => text.Contains(query, StringComparison.OrdinalIgnoreCase));

    private void ShowPage()
    {
        int pages = Math.Max(1, (_filtered.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);

        var rows = _filtered.Skip(_page * PageSize).Take(PageSize).Select(r => new Row(r)).ToList();
        Table.ItemsSource = rows;

        bool none = _all.Count == 0;
        bool noMatch = !none && rows.Count == 0;
        Table.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        Empty.Title = noMatch ? "No matching deployments" : "No deployments yet";
        Empty.Message = noMatch ? "Try a different search." : "History will appear after the first execution.";
        SearchRow.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        PagerRow.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        CountText.Text = _filtered.Count == _all.Count ? $"{_all.Count} deployment(s)" : $"{_filtered.Count} of {_all.Count} deployment(s)";
        PageText.Text = rows.Count == 0 ? "" : $"Showing {_page * PageSize + 1}–{_page * PageSize + rows.Count} of {_filtered.Count}";
        PrevButton.IsEnabled = _page > 0;
        NextButton.IsEnabled = _page < pages - 1;

        if (rows.Count == 0)
            Detail.Visibility = Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter(resetPage: true);
    }

    private void OnPrevClick(object sender, RoutedEventArgs e) { _page--; ShowPage(); }

    private void OnNextClick(object sender, RoutedEventArgs e) { _page++; ShowPage(); }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        Refresh();
        Message?.Invoke("History refreshed.");
    }

    // ---------- Detail ----------

    private void OnRowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Table.SelectedItem is Row row)
            ShowDetail(row.Record);
        else
            Detail.Visibility = Visibility.Collapsed;
    }

    private void ShowDetail(HistoryRecord r)
    {
        LeftColumn.Children.Clear();
        RightColumn.Children.Clear();
        WideRows.Children.Clear();

        AddRow(LeftColumn, "Execution ID", r.Id);
        AddRow(LeftColumn, "Started", r.StartedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        AddRow(LeftColumn, "Finished", r.FinishedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        AddRow(LeftColumn, "Operator", r.Operator);
        AddRow(LeftColumn, "Machine", r.Machine);

        AddRow(RightColumn, "Component", r.Component);
        AddRow(RightColumn, "Profile", r.Profile);
        AddRow(RightColumn, "Build version", r.BuildVersion);
        AddRow(RightColumn, "Operations", string.Join(", ", r.Operations));
        var pill = new StatusPill { Text = r.Outcome.ToString(), Kind = KindOf(r.Outcome) };
        RightColumn.Children.Add(new HeaderedContentControl { Style = (Style)FindResource("KeyValueRow"), Header = "Outcome", Content = pill });

        AddRow(WideRows, "Backup directory", r.BackupDirectory);

        bool hasError = r.ErrorMessage.Length > 0 || r.RestoreFailures.Count > 0;
        ErrorNotice.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = !hasError ? "" :
            (r.ErrorMessage.Length > 0 ? r.ErrorMessage : "") +
            (r.RestoreFailures.Count > 0
                ? (r.ErrorMessage.Length > 0 ? "\n\n" : "") + "Restore failures:\n" + string.Join("\n", r.RestoreFailures.Select(f => "• " + f))
                : "");

        bool hasBackup = r.BackupDirectory.Length > 0;
        bool exists = hasBackup && Directory.Exists(r.BackupDirectory);
        BackupRow.Visibility = hasBackup ? Visibility.Visible : Visibility.Collapsed;
        OpenBackupButton.IsEnabled = exists;
        BackupMissingText.Visibility = hasBackup && !exists ? Visibility.Visible : Visibility.Collapsed;
        OpenBackupButton.Tag = r.BackupDirectory;

        Detail.Visibility = Visibility.Visible;
    }

    private void AddRow(Panel panel, string label, string? value)
    {
        string text = string.IsNullOrEmpty(value) ? "—" : value;
        var content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, ToolTip = text };
        panel.Children.Add(new HeaderedContentControl { Style = (Style)FindResource("KeyValueRow"), Header = label, Content = content });
    }

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
}
