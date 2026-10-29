using System.Windows;
using System.Windows.Controls;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

public class AppServicesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));

    public AppServicesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteMachineIni(string environment = "Development")
    {
        string path = Path.Combine(_dir, "machine.ini");
        File.WriteAllText(path, $"[Meta]\nSchemaVersion=1.0\n[Machine]\nMachineName=M1\nEnvironment={environment}\n[InstallLocations]\nION=D:\\ION\n[PackMan]\nLocalCacheDirectory={_dir}\\Cache\n");
        return path;
    }

    [Fact]
    public void Create_WithValidFiles_WiresEverything()
    {
        var s = AppServices.Create(AppContext.BaseDirectory, WriteMachineIni());

        Assert.NotNull(s.Machine);
        Assert.NotNull(s.AllowList);
        Assert.NotNull(s.Profiles);
        Assert.NotNull(s.Builds);
        Assert.NotNull(s.History);
        Assert.Empty(s.MachineErrors);
        Assert.Empty(s.AllowListErrors);
    }

    [Fact]
    public void Create_WithBrokenFiles_KeepsReasons_AndDoesNotThrow()
    {
        var s = AppServices.Create(Path.Combine(_dir, "no-release"), Path.Combine(_dir, "nope.ini"));

        Assert.Null(s.Machine);
        Assert.Null(s.AllowList);
        Assert.Null(s.Profiles);
        Assert.Null(s.Builds);
        Assert.Null(s.History);
        Assert.Contains("file not found", Assert.Single(s.MachineErrors));
        Assert.Contains("file not found", Assert.Single(s.AllowListErrors));
        Assert.NotNull(s.Dashboard.Read());   // Dashboard tetap bisa dibuka
    }

    [Fact]
    public void EveryMenu_HasAScreen_WhenConfigIsValid()
    {
        WpfHost.Run(() =>
        {
            var s = AppServices.Create(AppContext.BaseDirectory, WriteMachineIni());
            var expected = new Dictionary<string, Type>
            {
                [MainWindow.Dashboard] = typeof(DashboardScreen),
                [MainWindow.Components] = typeof(ComponentsScreen),
                [MainWindow.Profiles] = typeof(ProfilesScreen),
                [MainWindow.Clients] = typeof(ClientsScreen),
                [MainWindow.History] = typeof(HistoryScreen),
                [MainWindow.DevConsole] = typeof(DevConsoleScreen),
            };

            foreach (var (key, type) in expected)
                Assert.IsType(type, ScreenFactory.Create(key, s, _ => { }));
        });
    }

    [Fact]
    public void BrokenConfig_GivesExplanationScreens_InsteadOfCrashing()
    {
        WpfHost.Run(() =>
        {
            var s = AppServices.Create(Path.Combine(_dir, "no-release"), Path.Combine(_dir, "nope.ini"));

            foreach (string key in new[] { MainWindow.Components, MainWindow.Profiles, MainWindow.History, MainWindow.DevConsole })
                Assert.IsType<PlaceholderScreen>(ScreenFactory.Create(key, s, _ => { }));
            Assert.IsType<DashboardScreen>(ScreenFactory.Create(MainWindow.Dashboard, s, _ => { }));
        });
    }

    [Fact]
    public void FullWindow_NavigatesAllMenus_WithRealScreens()
    {
        string png = Path.Combine(Path.GetTempPath(), "ionpackman-app-dashboard.png");

        WpfHost.Run(() =>
        {
            var s = AppServices.Create(AppContext.BaseDirectory, WriteMachineIni());
            MainWindow window = null!;
            window = new MainWindow(key => ScreenFactory.Create(key, s, t => window.ShowToast(t)));
            window.SetMachine(s.Machine, s.MachineErrors);

            foreach (var (key, _, _) in MainWindow.Menus)
                window.Navigate(key);
            Assert.Equal(MainWindow.History, window.CurrentKey);   // menu terakhir dalam daftar

            window.Navigate(MainWindow.Dashboard);
            WpfHost.RenderWindowToPng(window, 1024, 768, png);
            window.Close();
        });

        Assert.True(File.Exists(png));
    }

    [Fact]
    public void UseInDeveloperConsole_OpensTheConsole_WithTheComponentSelected()
    {
        WpfHost.Run(() =>
        {
            var s = AppServices.Create(AppContext.BaseDirectory, WriteMachineIni());
            // Dirangkai persis seperti App.OnStartup: 'window' hanya dipakai lewat lambda (method group akan crash karena masih null).
            MainWindow window = null!;
            window = new MainWindow(key => ScreenFactory.Create(key, s, t => window.ShowToast(t), k => window.Open(k)));
            window.SetMachine(s.Machine, s.MachineErrors);

            window.Navigate(MainWindow.Components);
            var components = (ComponentsScreen)window.CurrentScreen;
            components.OpenComponent("wind");
            ((Button)components.FindName("UseInConsoleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(MainWindow.DevConsole, window.CurrentKey);
            var console = Assert.IsType<DevConsoleScreen>(window.CurrentScreen);
            Assert.Equal("wind", ((ComboBox)console.FindName("ComponentBox")).SelectedItem);
            window.Close();
        });
    }

    [Fact]
    public void UseInDeveloperConsole_OnANonDevelopmentMachine_ExplainsInsteadOfOpening()
    {
        WpfHost.Run(() =>
        {
            var s = AppServices.Create(AppContext.BaseDirectory, WriteMachineIni("Production"));
            MainWindow window = null!;
            window = new MainWindow(key => ScreenFactory.Create(key, s, t => window.ShowToast(t), k => window.Open(k)));
            window.SetMachine(s.Machine, s.MachineErrors);

            window.Navigate(MainWindow.Components);
            var components = (ComponentsScreen)window.CurrentScreen;
            components.OpenComponent("wind");
            ((Button)components.FindName("UseInConsoleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(MainWindow.Components, window.CurrentKey);   // tetap di Components
            Assert.Equal("Only available on Development machines (this machine is 'Production')", ((IonPackMan.Controls.Toast)window.FindName("ToastBox")).Message);
            window.Close();
        });
    }
}
