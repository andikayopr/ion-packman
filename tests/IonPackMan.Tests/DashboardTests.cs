using System.Windows;
using System.Windows.Controls;
using IonPackMan.Components;
using IonPackMan.Controls;
using IonPackMan.History;
using IonPackMan.Machine;
using IonPackMan.Profiles;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

public class DashboardTests : IDisposable
{
    // Data awal yang ikut ke output (Components/, Profiles/, Config/) dipakai apa adanya, hanya dibaca.
    private static readonly string ReleaseDir = AppContext.BaseDirectory;
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));

    public DashboardTests() => Directory.CreateDirectory(_cache);
    public void Dispose() => Directory.Delete(_cache, recursive: true);

    private MachineConfig Machine() => new()
    {
        MachineName = "ATM-DEV-001", Environment = "Development",
        InstallLocations = new Dictionary<string, string> { ["ION"] = @"D:\ION" },
        LocalCacheDirectory = _cache,
    };

    // Hanya komponen QRIS TAP yang "ada" di mesin palsu ini, jadi Profile terdeteksi = QRISTAP.
    private static readonly string[] QrisDirs = [@"D:\ION\IonMiddleware", @"D:\APCONTAINER\WIND", @"D:\ION\QRAPTOR"];

    private DashboardReader Reader(MachineConfig? machine, IReadOnlyList<string>? errors = null, HistoryStore? history = null)
    {
        var components = new ComponentStore(ReleaseDir);
        var (allow, allowErrors) = AllowList.Load(Paths.AllowListIni(ReleaseDir));
        Assert.Empty(allowErrors);
        var validator = new ProfileValidator(components, allow!);
        var profiles = new ProfileStore(ReleaseDir, validator);
        var detector = new ProfileDetector(components, profiles,
            directoryExists: d => QrisDirs.Contains(d, StringComparer.OrdinalIgnoreCase),
            serviceExists: s => s is "IonMiddleware" or "WIND");
        return new DashboardReader(machine, errors ?? [], new MachineInspector(), detector, profiles, validator, components, history);
    }

    [Fact]
    public void Read_WithoutMachineIni_ShowsUnknownAndTheReason_NotSampleData()
    {
        var snapshot = Reader(null, ["machine.ini: file not found."]).Read();

        Assert.Equal(Environment.MachineName, snapshot.MachineName);
        Assert.Equal("Unknown", snapshot.Environment);
        Assert.Equal("Unknown", snapshot.ProfileText);
        Assert.Equal(StatusKind.Warning, snapshot.ProfileKind);
        Assert.Contains("file not found", snapshot.ProfileReason);
        Assert.Equal("machine.ini not valid", snapshot.ProfileConfig);
        Assert.Equal("Unknown", snapshot.DeploymentState);
        Assert.Empty(snapshot.Services);
    }

    [Fact]
    public void Read_DetectedProfile_ListsItsServicesAndProcesses()
    {
        var snapshot = Reader(Machine()).Read();

        Assert.Equal("QRIS TAP", snapshot.ProfileText);
        Assert.Equal(StatusKind.Success, snapshot.ProfileKind);
        Assert.Equal("Valid", snapshot.ProfileConfig);
        Assert.Equal("Development", snapshot.Environment);

        // Service yang dideklarasikan component tetapi tidak ada di mesin ini ditandai, bukan disembunyikan.
        var declared = snapshot.Services.Where(r => r.Component != "—").ToList();
        Assert.Equal(new[] { "ion-middleware", "wind" }, declared.Select(r => r.Component));
        Assert.All(declared, r => Assert.Equal(("Not found", StatusKind.Warning), (r.Status, r.Kind)));

        // Proses: satu baris "Not detected" per component yang tidak berjalan.
        Assert.Equal(new[] { "ion-middleware", "wind", "qraptor" }, snapshot.Processes.Select(r => r.Component));
        Assert.All(snapshot.Processes, r => Assert.Equal("Not detected", r.Status));
    }

    [Fact]
    public void Read_DeploymentState_ComesFromTheLatestHistoryRecord()
    {
        var history = new HistoryStore(_cache);
        Assert.Equal("No deployments yet", Reader(Machine(), history: history).Read().DeploymentState);

        Assert.Empty(history.Write(new HistoryRecord
        {
            Id = HistoryRecord.NewExecId("wind"), StartedAt = DateTimeOffset.Now, Operator = "dev", Machine = "ATM-DEV-001",
            Profile = "QRISTAP", Component = "wind", BuildVersion = "2.1.0", Outcome = HistoryOutcome.FailedRestored,
        }));

        var snapshot = Reader(Machine(), history: history).Read();
        Assert.Equal("FailedRestored", snapshot.DeploymentState);
        Assert.Equal(StatusKind.Error, snapshot.DeploymentKind);
        Assert.Contains("wind", snapshot.LastDeployment);
    }

    [Fact]
    public void Screen_ShowsTheSnapshot_FiltersAndRenders()
    {
        string png = Path.Combine(Path.GetTempPath(), "ionpackman-dashboard.png");

        WpfHost.Run(() =>
        {
            var screen = new DashboardScreen(Reader(Machine()));
            var host = new Window { Content = screen };
            screen.Refresh();

            Assert.Equal(Environment.MachineName, ((TextBlock)screen.FindName("MachineName")).Text);   // nama komputer sebenarnya
            Assert.Equal("QRIS TAP", ((TextBlock)screen.FindName("ProfileName")).Text);
            Assert.Equal("Detected", ((StatusPill)screen.FindName("ProfilePill")).Text);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)screen.FindName("ProblemNotice")).Visibility);

            var processes = (DataGrid)screen.FindName("ProcessesGrid");
            Assert.Equal(3, processes.Items.Count);
            ((TextBox)screen.FindName("SearchBox")).Text = "wind";
            Assert.Equal(1, processes.Items.Count);
            ((TextBox)screen.FindName("SearchBox")).Text = "";

            host.Content = null;   // RenderToPng memasang layar ke jendelanya sendiri
            WpfHost.RenderToPng(screen, 840, png);
        });

        Assert.True(File.Exists(png));
    }

    [Fact]
    public void Screen_WhenProfileIsUnknown_ExplainsWhy()
    {
        WpfHost.Run(() =>
        {
            var screen = new DashboardScreen(Reader(null, ["machine.ini: file not found."]));
            _ = new Window { Content = screen };
            screen.Refresh();

            var notice = (Notice)screen.FindName("ProblemNotice");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Contains("file not found", ((TextBlock)notice.Content).Text);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)screen.FindName("ServicesEmpty")).Visibility);
        });
    }

    [Fact]
    public void Screen_Refresh_RaisesAToast()
    {
        WpfHost.Run(() =>
        {
            var screen = new DashboardScreen(Reader(Machine()));
            _ = new Window { Content = screen };
            string? message = null;
            screen.Message += m => message = m;

            ((Button)screen.FindName("RefreshButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal("Machine status refreshed.", message);
        });
    }
}
