using System.Windows.Controls;
using IonPackMan.Machine;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>ADR-006: ClientConfig.Save dan layar Clients (lihat machine.ini, edit clients.ini).</summary>
public class ClientsScreenTests : IDisposable
{
    private static readonly string[] Known = ["wind", "qraptor", "ion-middleware"];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-clients-screen-" + Guid.NewGuid().ToString("N"));

    public ClientsScreenTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Components"));
        Directory.CreateDirectory(Path.Combine(_dir, "Config"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Components", "catalog.ini"), Path.Combine(_dir, "Components", "catalog.ini"));
        File.WriteAllText(Path.Combine(_dir, "Config", "clients.ini"),
            "[Meta]\nSchemaVersion=1.0\n[Clients]\nBCA=Bank BCA\nBRI=Bank BRI\n[Overrides]\nBCA=wind,qraptor\nBRI=ion-middleware\n");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string ClientsPath => Path.Combine(_dir, "Config", "clients.ini");

    private string MachineIni(string environment = "Development", string client = "")
    {
        string path = Path.Combine(_dir, "machine.ini");
        File.WriteAllText(path, $"[Meta]\nSchemaVersion=1.0\n[Machine]\nMachineName=M1\nEnvironment={environment}\nClient={client}\n[InstallLocations]\nION=D:\\ION\n[PackMan]\nLocalCacheDirectory={_dir}\\Cache\n");
        return path;
    }

    private AppServices Services(string environment = "Development", string client = "") => AppServices.Create(_dir, MachineIni(environment, client));

    private static T Find<T>(ClientsScreen s, string name) where T : class => (T)s.FindName(name);

    // ---------- ClientConfig.Save ----------

    [Fact]
    public void Save_RoundTrips_ThroughLoad()
    {
        var (config, _) = ClientConfig.Load(ClientsPath, Known);
        var updated = config!.With("BNI", "Bank BNI", ["wind"]).Without("BRI");
        string path = Path.Combine(_dir, "out.ini");

        Assert.Empty(updated.Save(path, Known));
        var (loaded, errors) = ClientConfig.Load(path, Known);

        Assert.Empty(errors);
        Assert.Equal(["BCA", "BNI"], loaded!.Clients.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Bank BNI", loaded.Clients["BNI"]);
        Assert.Equal(["qraptor", "wind"], loaded.OverriddenBy("BCA"));
        Assert.Equal(["wind"], loaded.OverriddenBy("BNI"));
        Assert.False(loaded.IsKnown("BRI"));
    }

    [Fact]
    public void Save_RejectsUnknownComponentAndBadId_WithoutTouchingTheFile()
    {
        var (config, _) = ClientConfig.Load(ClientsPath, Known);
        string before = File.ReadAllText(ClientsPath);

        var unknownComponent = config!.With("BNI", "Bank BNI", ["no-such-component"]).Save(ClientsPath, Known);
        var badId = config.With("B@I", "x", []).Save(ClientsPath, Known);

        Assert.Contains(unknownComponent, e => e.Contains("no-such-component"));
        Assert.Contains(badId, e => e.Contains("not a valid Client Id"));
        Assert.Equal(before, File.ReadAllText(ClientsPath));
    }

    [Fact]
    public void Replace_UpdatesTheSharedInstance_ButNotNone()
    {
        var shared = new ClientConfig();
        var (config, _) = ClientConfig.Load(ClientsPath, Known);

        shared.Replace(config!);

        Assert.True(shared.IsKnown("BCA"));
        Assert.Throws<InvalidOperationException>(() => ClientConfig.None.Replace(config!));
    }

    // ---------- Layar ----------

    [Fact]
    public void Screen_OnDevelopmentMachine_SavesNewClient_AndSharedConfigSeesIt()
    {
        WpfHost.Run(() =>
        {
            var services = Services();
            var screen = new ClientsScreen(services);

            screen.NewClient();
            Find<TextBox>(screen, "IdBox").Text = "BNI";
            Find<TextBox>(screen, "NameBox").Text = "Bank BNI";
            var wind = Find<StackPanel>(screen, "ComponentList").Children.OfType<CheckBox>().Single(c => (string)c.Tag == "wind");
            wind.IsChecked = true;

            Assert.True(screen.Save());

            Assert.True(services.Clients.IsKnown("BNI"));
            Assert.True(services.Clients.Overrides("BNI", "wind"));
            Assert.Equal("BNI", screen.SelectedClient);
            Assert.Contains("BNI=Bank BNI", File.ReadAllText(ClientsPath));
        });
    }

    [Fact]
    public void Screen_RejectsDuplicateAndInvalidId()
    {
        WpfHost.Run(() =>
        {
            var screen = new ClientsScreen(Services());

            screen.NewClient();
            Find<TextBox>(screen, "IdBox").Text = "bca";
            Assert.False(screen.Save());   // sudah ada (tidak peka huruf besar/kecil)

            Find<TextBox>(screen, "IdBox").Text = "not valid";
            Assert.False(screen.Save());
            Assert.Equal(System.Windows.Visibility.Visible, Find<IonPackMan.Controls.Notice>(screen, "ErrorNotice").Visibility);
        });
    }

    [Fact]
    public void Delete_IsRefused_ForTheClientOfThisMachine_AndAllowedOtherwise()
    {
        WpfHost.Run(() =>
        {
            var services = Services(client: "BCA");
            var screen = new ClientsScreen(services, _ => true);

            screen.Select("BCA");
            Assert.False(screen.Delete());
            Assert.True(services.Clients.IsKnown("BCA"));

            screen.Select("BRI");
            Assert.True(screen.Delete());
            Assert.False(services.Clients.IsKnown("BRI"));
            Assert.DoesNotContain("BRI", File.ReadAllText(ClientsPath));
        });
    }

    [Fact]
    public void Screen_OnNonDevelopmentMachine_ShowsButDoesNotEdit()
    {
        WpfHost.Run(() =>
        {
            var services = Services("Test");
            var screen = new ClientsScreen(services);
            string before = File.ReadAllText(ClientsPath);

            screen.Select("BCA");
            Find<TextBox>(screen, "NameBox").Text = "Changed";

            Assert.False(Find<Button>(screen, "SaveButton").IsEnabled);
            Assert.False(Find<Button>(screen, "NewButton").IsEnabled);
            Assert.False(Find<ComboBox>(screen, "MachineClientBox").IsEnabled);
            Assert.False(screen.Save());
            Assert.Equal(before, File.ReadAllText(ClientsPath));
        });
    }

    [Fact]
    public void MachineClientBox_SavesClientToMachineIni()
    {
        WpfHost.Run(() =>
        {
            var services = Services();
            var screen = new ClientsScreen(services);

            Find<ComboBox>(screen, "MachineClientBox").SelectedItem = "BRI";

            Assert.Equal("BRI", services.Machine!.Client);
            Assert.Contains("Client=BRI", File.ReadAllText(Path.Combine(_dir, "machine.ini")));
        });
    }
}
