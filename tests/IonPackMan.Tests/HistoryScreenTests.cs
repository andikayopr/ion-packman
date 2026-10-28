using System.Windows;
using System.Windows.Controls;
using IonPackMan.Controls;
using IonPackMan.History;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

public class HistoryScreenTests : IDisposable
{
    // Area konten di 1024×768: 1024 − sidebar 184 − padding 2×16 ≈ 808 (UI/UX Spec §1).
    private const double ContentWidth = 808 + 32;

    private readonly string _cache = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));
    private readonly HistoryStore _store;

    public HistoryScreenTests() => _store = new HistoryStore(_cache);

    public void Dispose()
    {
        if (Directory.Exists(_cache))
            Directory.Delete(_cache, recursive: true);
    }

    private static readonly DateTimeOffset Base = new(2026, 10, 28, 8, 0, 0, TimeSpan.FromHours(7));
    private static readonly string[] Components = ["wind", "qraptor", "mdd-service", "ion-middleware"];
    private static readonly HistoryOutcome[] Outcomes =
        [HistoryOutcome.Succeeded, HistoryOutcome.Rejected, HistoryOutcome.FailedRestored, HistoryOutcome.FailedRestoreIncomplete, HistoryOutcome.RestoredAfterCrash];

    // Data contoh: n eksekusi, i = 0 paling lama. Eksekusi ke-3 (gagal) punya backup folder nyata.
    private void Seed(int n)
    {
        for (int i = 0; i < n; i++)
        {
            var started = Base.AddMinutes(i * 25);
            string component = Components[i % Components.Length];
            var outcome = Outcomes[i % Outcomes.Length];
            string id = HistoryRecord.NewExecId(component, started);
            string backup = Path.Combine(_cache, "Backups", id);
            if (i == 3)
                Directory.CreateDirectory(backup);

            bool failed = outcome is HistoryOutcome.FailedRestored or HistoryOutcome.FailedRestoreIncomplete;
            var errors = _store.Write(new HistoryRecord
            {
                Id = id, StartedAt = started, FinishedAt = started.AddSeconds(72), Operator = @"ATM-DEV-001\developer", Machine = "ATM-DEV-001",
                Profile = i % 2 == 0 ? "QRISTAP" : "MDD", Component = component, BuildVersion = $"2.1.{i}-dev.{17 + i}",
                Operations = ["DeployFiles", "ApplyConfig"], Outcome = outcome, BackupDirectory = backup,
                ErrorMessage = failed ? "Copy failed: access to 'D:\\APCONTAINER\\WIND\\new.dll' is denied.\nThe service was restarted with the previous files." : "",
                RestoreFailures = outcome == HistoryOutcome.FailedRestoreIncomplete ? [@"D:\APCONTAINER\WIND\a.dll: file is in use", @"D:\APCONTAINER\WIND\b.dll: file is in use"] : [],
            });
            Assert.Empty(errors);
        }
    }

    // Style implisit hanya berlaku bila elemen berada di dalam Window (lihat ThemeTests.Realize).
    private static HistoryScreen Realize(HistoryScreen screen)
    {
        _ = new Window { Content = screen };
        screen.Measure(new Size(ContentWidth, 2000));
        return screen;
    }

    // Isi panel detail dibuat lewat kode; template-nya baru diterapkan saat tata letak berjalan, jadi baca pohon logis.
    private static IEnumerable<T> Logical<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;
            foreach (var nested in Logical<T>(child))
                yield return nested;
        }
    }

    private static T Part<T>(HistoryScreen s, string name) where T : class => (T)s.FindName(name);

    private static List<HistoryScreen.Row> Rows(HistoryScreen s) =>
        ((IEnumerable<HistoryScreen.Row>)Part<DataGrid>(s, "Table").ItemsSource).ToList();

    [Fact]
    public void Empty_ShowsEmptyState_AndNoTable()
    {
        WpfHost.Run(() =>
        {
            var screen = Realize(new HistoryScreen(_store));

            var empty = Part<EmptyState>(screen, "Empty");
            Assert.Equal(Visibility.Visible, empty.Visibility);
            Assert.Equal("No deployments yet", empty.Title);
            Assert.Equal(Visibility.Collapsed, Part<DataGrid>(screen, "Table").Visibility);
            Assert.Equal(Visibility.Collapsed, Part<FrameworkElement>(screen, "Detail").Visibility);
            Assert.Equal(Visibility.Collapsed, Part<FrameworkElement>(screen, "ProblemsNotice").Visibility);
            Assert.Empty(Rows(screen));
        });
    }

    [Fact]
    public void Filled_ShowsNewestFirst_WithTheRightColumnsAndPills()
    {
        Seed(5);
        WpfHost.Run(() =>
        {
            var screen = Realize(new HistoryScreen(_store));

            Assert.Equal(Visibility.Collapsed, Part<EmptyState>(screen, "Empty").Visibility);
            var rows = Rows(screen);
            Assert.Equal(5, rows.Count);
            Assert.Equal("2.1.4-dev.21", rows[0].Build);                 // terbaru di atas
            Assert.Equal("2026-10-28 09:40:00", rows[0].Time);
            Assert.Equal("RestoredAfterCrash", rows[0].OutcomeText);
            Assert.Equal(StatusKind.Info, rows[0].Kind);
            Assert.Equal("wind", rows[^1].Component);

            Assert.Equal(StatusKind.Success, HistoryScreen.KindOf(HistoryOutcome.Succeeded));
            Assert.Equal(StatusKind.Warning, HistoryScreen.KindOf(HistoryOutcome.Rejected));
            Assert.Equal(StatusKind.Error, HistoryScreen.KindOf(HistoryOutcome.FailedRestored));
            Assert.Equal(StatusKind.Error, HistoryScreen.KindOf(HistoryOutcome.FailedRestoreIncomplete));
            Assert.Equal(StatusKind.Info, HistoryScreen.KindOf(HistoryOutcome.RestoredAfterCrash));

            Assert.Equal(["Time", "Component", "Build", "Outcome"], Part<DataGrid>(screen, "Table").Columns.Select(c => c.Header));
        });
    }

    [Fact]
    public void Search_FiltersRows_ResetsToFirstPage_AndShowsNoMatchState()
    {
        Seed(12);
        WpfHost.Run(() =>
        {
            var screen = Realize(new HistoryScreen(_store));
            var search = Part<TextBox>(screen, "SearchBox");

            search.Text = "QRAPTOR";                                     // tidak peka huruf besar-kecil
            var rows = Rows(screen);
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.Equal("qraptor", r.Component));

            search.Text = "FailedRestoreIncomplete";
            Assert.All(Rows(screen), r => Assert.Equal("FailedRestoreIncomplete", r.OutcomeText));

            search.Text = "no-such-thing";
            Assert.Empty(Rows(screen));
            var empty = Part<EmptyState>(screen, "Empty");
            Assert.Equal(Visibility.Visible, empty.Visibility);
            Assert.Equal("No matching deployments", empty.Title);

            search.Text = "";
            Assert.Equal(8, Rows(screen).Count);
            Assert.Equal("No deployments yet", empty.Title);
        });
    }

    [Fact]
    public void Paging_Shows8RowsPerPage_AndNavigates()
    {
        Seed(12);
        WpfHost.Run(() =>
        {
            var screen = Realize(new HistoryScreen(_store));
            var prev = Part<Button>(screen, "PrevButton");
            var next = Part<Button>(screen, "NextButton");
            var text = Part<TextBlock>(screen, "PageText");

            Assert.Equal(8, Rows(screen).Count);
            Assert.False(prev.IsEnabled);
            Assert.True(next.IsEnabled);
            Assert.Equal("Showing 1–8 of 12", text.Text);
            string firstOnPage1 = Rows(screen)[0].Record.Id;

            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4, Rows(screen).Count);
            Assert.NotEqual(firstOnPage1, Rows(screen)[0].Record.Id);
            Assert.True(prev.IsEnabled);
            Assert.False(next.IsEnabled);
            Assert.Equal("Showing 9–12 of 12", text.Text);

            prev.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(firstOnPage1, Rows(screen)[0].Record.Id);
        });
    }

    [Fact]
    public void ClickingARow_ShowsDetailBelowTheTable_WithErrorAndBackupButton()
    {
        Seed(5);
        WpfHost.Run(() =>
        {
            var screen = Realize(new HistoryScreen(_store));
            var table = Part<DataGrid>(screen, "Table");
            var detail = Part<FrameworkElement>(screen, "Detail");
            Assert.Equal(Visibility.Collapsed, detail.Visibility);

            // Baris ke-2 (indeks 1) = eksekusi i=3: FailedRestoreIncomplete, backup folder ada.
            table.SelectedIndex = 1;
            Assert.Equal(Visibility.Visible, detail.Visibility);
            var texts = Logical<TextBlock>(detail).Select(t => t.Text).ToList();
            Assert.Contains("ATM-DEV-001\\developer", texts);
            Assert.Contains("2.1.3-dev.20", texts);
            Assert.Contains("DeployFiles, ApplyConfig", texts);
            Assert.Contains(Logical<StatusPill>(detail), p => p.Text == "FailedRestoreIncomplete" && p.Kind == StatusKind.Error);

            Assert.Equal(Visibility.Visible, Part<FrameworkElement>(screen, "ErrorNotice").Visibility);
            string error = Part<TextBlock>(screen, "ErrorText").Text;
            Assert.Contains("Copy failed", error);
            Assert.Contains("a.dll: file is in use", error);
            Assert.True(Part<Button>(screen, "OpenBackupButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Part<FrameworkElement>(screen, "BackupMissingText").Visibility);

            // Baris sukses tanpa pesan error, backup folder tidak ada: tombol nonaktif + keterangan.
            table.SelectedIndex = 4;
            Assert.Equal(Visibility.Collapsed, Part<FrameworkElement>(screen, "ErrorNotice").Visibility);
            Assert.False(Part<Button>(screen, "OpenBackupButton").IsEnabled);
            Assert.Equal(Visibility.Visible, Part<FrameworkElement>(screen, "BackupMissingText").Visibility);

            table.SelectedIndex = -1;
            Assert.Equal(Visibility.Collapsed, detail.Visibility);
        });
    }

    [Fact]
    public void Refresh_PicksUpNewFiles_AndReportsBrokenOnesWithoutLosingTheList()
    {
        Seed(2);
        WpfHost.Run(() =>
        {
            var screen = Realize(new HistoryScreen(_store));
            Assert.Equal(2, Rows(screen).Count);

            File.WriteAllText(Paths.HistoryIni(_cache, "broken"), "not ini at all");
            _store.Write(new HistoryRecord
            {
                Id = "20261029-080000-wind-0001", StartedAt = Base.AddDays(1), Operator = "o", Machine = "m", Component = "wind",
                Outcome = HistoryOutcome.Succeeded,
            });
            screen.Refresh();

            Assert.Equal(3, Rows(screen).Count);
            Assert.Equal("20261029-080000-wind-0001", Rows(screen)[0].Record.Id);
            Assert.Equal(Visibility.Visible, Part<FrameworkElement>(screen, "ProblemsNotice").Visibility);
            Assert.Contains("broken.ini", Part<TextBlock>(screen, "ProblemsText").Text);
        });
    }

    [Fact]
    public void RefreshButton_RaisesMessageEvent()
    {
        WpfHost.Run(() =>
        {
            var screen = Realize(new HistoryScreen(_store));
            string? message = null;
            screen.Message += m => message = m;

            Part<Button>(screen, "RefreshButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal("History refreshed.", message);
        });
    }

    [Fact]
    public void Renders_EmptyAndTwelveRows_FitContentWidth_AndWritePngs()
    {
        string dir = Environment.GetEnvironmentVariable("HISTORY_PNG_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        string emptyPng = Path.Combine(dir, "history-empty.png");
        string filledPng = Path.Combine(dir, "history-12rows.png");
        string detailPng = Path.Combine(dir, "history-12rows-detail.png");

        var (emptyW, _) = WpfHost.Run(() => WpfHost.RenderToPng(new HistoryScreen(_store), ContentWidth, emptyPng));

        Seed(12);
        var (filledW, _) = WpfHost.Run(() => WpfHost.RenderToPng(new HistoryScreen(_store), ContentWidth, filledPng));
        var (detailW, detailH) = WpfHost.Run(() =>
        {
            var screen = new HistoryScreen(_store);
            Part<DataGrid>(screen, "Table").SelectedIndex = 1;
            return WpfHost.RenderToPng(screen, ContentWidth, detailPng);
        });

        foreach (var png in new[] { emptyPng, filledPng, detailPng })
            Assert.True(File.Exists(png) && new FileInfo(png).Length > 3_000, $"{png} was not written");
        Assert.All(new[] { emptyW, filledW, detailW }, w => Assert.True(w <= ContentWidth + 0.5, $"screen is {w}px wide, wider than {ContentWidth}px"));
        Assert.True(detailH > 400);
    }
}
