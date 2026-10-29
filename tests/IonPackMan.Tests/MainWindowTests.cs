using System.Windows;
using System.Windows.Controls;
using IonPackMan.Machine;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

public class MainWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));

    public MainWindowTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeScreen : UserControl, IScreen, IListDetailScreen
    {
        public event Action<string>? Message;
        public int Refreshed;
        public int ListShown;
        public void Refresh() => Refreshed++;
        public void ShowList() => ListShown++;
        public void Say(string text) => Message?.Invoke(text);
    }

    [Fact]
    public void ClickingTheCurrentMenuAgain_ReturnsAListDetailScreenToItsList_OtherClicksDoNot()
    {
        WpfHost.Run(() =>
        {
            var made = new Dictionary<string, FakeScreen>();
            var w = Create(made);
            var nav = Find<StackPanel>(w, "Nav");
            var profiles = (RadioButton)nav.Children[2];

            profiles.IsChecked = true;                      // pindah ke Profiles: bukan klik pada menu yang sudah tampil
            Assert.Equal(0, made["profiles"].ListShown);

            profiles.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(1, made["profiles"].ListShown);    // klik lagi: kembali ke daftar
        });
    }

    private static MachineConfig Machine(string environment) => new()
    {
        MachineName = "ATM-DEV-001", Environment = environment,
        InstallLocations = new Dictionary<string, string>(), LocalCacheDirectory = @"D:\ION-PackMan\Cache",
    };

    private static MainWindow Create(Dictionary<string, FakeScreen>? made = null) => new(key =>
    {
        var screen = new FakeScreen();
        made?.Add(key, screen);
        return screen;
    });

    private static T Find<T>(MainWindow w, string name) where T : class => (T)w.FindName(name);

    [Fact]
    public void HasTheSixMenus_AndStartsOnDashboard()
    {
        WpfHost.Run(() =>
        {
            var w = Create();

            Assert.Equal(new[] { "dashboard", "components", "profiles", "clients", "devconsole", "history" }, MainWindow.Menus.Select(m => m.Key));
            Assert.Equal(6, Find<StackPanel>(w, "Nav").Children.Count);
            Assert.Equal("dashboard", w.CurrentKey);
            Assert.Equal("Dashboard", Find<System.Windows.Documents.Run>(w, "CrumbText").Text);
            Assert.DoesNotContain(MainWindow.Menus, m => m.Title.Contains("Release"));   // menu Release = Tahap 2
        });
    }

    [Fact]
    public void ClickingAMenu_ShowsItsScreen_AndUpdatesTheBreadcrumb()
    {
        WpfHost.Run(() =>
        {
            var made = new Dictionary<string, FakeScreen>();
            var w = Create(made);
            var nav = Find<StackPanel>(w, "Nav");

            ((RadioButton)nav.Children[2]).IsChecked = true;   // Profiles

            Assert.Equal("profiles", w.CurrentKey);
            Assert.Same(made["profiles"], w.CurrentScreen);
            Assert.Equal("Profiles", Find<System.Windows.Documents.Run>(w, "CrumbText").Text);
            Assert.True(((RadioButton)nav.Children[2]).IsChecked);
            Assert.False(((RadioButton)nav.Children[0]).IsChecked);
        });
    }

    [Fact]
    public void Screens_AreCreatedOnce_AndRefreshedEveryTimeTheyOpen()
    {
        WpfHost.Run(() =>
        {
            var made = new Dictionary<string, FakeScreen>();
            var w = Create(made);

            w.Navigate(MainWindow.History);
            w.Navigate(MainWindow.Dashboard);
            w.Navigate(MainWindow.History);

            Assert.Equal(2, made.Count);                  // dashboard + history, bukan 3
            Assert.Equal(2, made["history"].Refreshed);
            Assert.Equal(2, made["dashboard"].Refreshed);
        });
    }

    [Fact]
    public void ScreenMessage_BecomesAToast()
    {
        WpfHost.Run(() =>
        {
            var made = new Dictionary<string, FakeScreen>();
            var w = Create(made);
            var toast = Find<IonPackMan.Controls.Toast>(w, "ToastBox");
            Assert.Equal(Visibility.Collapsed, toast.Visibility);

            made["dashboard"].Say("Saved.");

            Assert.Equal(Visibility.Visible, toast.Visibility);
            Assert.Equal("Saved.", toast.Message);
        });
    }

    [Fact]
    public void DeveloperConsole_IsLockedUnlessEnvironmentIsDevelopment()
    {
        WpfHost.Run(() =>
        {
            var w = Create();

            w.SetMachine(Machine("Development"), []);
            Assert.True(w.IsMenuEnabled(MainWindow.DevConsole));
            Assert.Equal("ATM-DEV-001 · Development", Find<TextBlock>(w, "MachineText").Text);

            w.SetMachine(Machine("Production"), []);
            Assert.False(w.IsMenuEnabled(MainWindow.DevConsole));
            Assert.Equal("Only available on Development machines (this machine is 'Production')", ((RadioButton)Find<StackPanel>(w, "Nav").Children[4]).ToolTip);

            w.SetMachine(null, ["machine.ini: file not found."]);   // tidak terbaca = terkunci juga
            Assert.False(w.IsMenuEnabled(MainWindow.DevConsole));
            Assert.Equal("machine.ini is not valid", Find<TextBlock>(w, "MachineText").Text);
            // Alasan terkunci menyebut masalah sebenarnya, bukan "bukan Development".
            Assert.Contains("machine.ini could not be loaded (machine.ini: file not found.)", (string)((RadioButton)Find<StackPanel>(w, "Nav").Children[4]).ToolTip);
            Assert.Contains("file not found", (string)Find<TextBlock>(w, "MachineText").ToolTip);
        });
    }

    [Fact]
    public void WhenMachineBecomesLocked_WhileOnDeveloperConsole_ItReturnsToDashboard()
    {
        WpfHost.Run(() =>
        {
            var w = Create();
            w.SetMachine(Machine("Development"), []);
            w.Navigate(MainWindow.DevConsole);

            w.SetMachine(Machine("Production"), []);

            Assert.Equal(MainWindow.Dashboard, w.CurrentKey);
        });
    }

    [Fact]
    public void Sidebar_CollapsesToIconsBelow900_AndExpandsAgain()
    {
        string png1024 = Path.Combine(Path.GetTempPath(), "ionpackman-shell-1024.png");
        string png800 = Path.Combine(Path.GetTempPath(), "ionpackman-shell-800.png");

        WpfHost.Run(() =>
        {
            var w = Create();
            w.SetMachine(Machine("Development"), []);
            WpfHost.RenderWindowToPng(w, 1024, 700, png1024);
            Assert.False(w.IsSidebarCollapsed);
            Assert.Equal(184, Find<ColumnDefinition>(w, "SidebarColumn").ActualWidth, 1);

            w.Width = 800;
            w.UpdateLayout();
            Assert.True(w.IsSidebarCollapsed);
            Assert.Equal(60, Find<ColumnDefinition>(w, "SidebarColumn").ActualWidth, 1);
            Assert.All(Find<StackPanel>(w, "Nav").Children.Cast<RadioButton>(),
                       b => Assert.Equal(Visibility.Collapsed, ((StackPanel)b.Content).Children[1].Visibility));

            w.Width = 1024;
            w.UpdateLayout();
            Assert.False(w.IsSidebarCollapsed);
            w.Close();
        });

        Assert.True(File.Exists(png1024) && new FileInfo(png1024).Length > 3_000);
    }

    [Fact]
    public void Render_Collapsed800_WritesPreview()
    {
        string png = Path.Combine(Path.GetTempPath(), "ionpackman-shell-800.png");
        WpfHost.Run(() =>
        {
            var w = Create();
            w.SetMachine(Machine("Development"), []);
            WpfHost.RenderWindowToPng(w, 800, 600, png);
            Assert.True(w.IsSidebarCollapsed);
            w.Close();
        });
        Assert.True(File.Exists(png));
    }

    [Fact]
    public void Placeholder_ShowsTitleAndMessage()
    {
        WpfHost.Run(() =>
        {
            var screen = new PlaceholderScreen("History", "Not built yet.");
            _ = new Window { Content = screen };
            screen.Measure(new Size(800, 600));

            var texts = WpfHost.Descendants<TextBlock>(screen).Select(t => t.Text).ToList();
            Assert.Contains("History", texts);
            Assert.Contains("Not built yet.", texts);
        });
    }
}
