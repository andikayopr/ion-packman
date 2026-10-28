using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IonPackMan.Machine;
using IonPackMan.Screens;

namespace IonPackMan;

/// <summary>Kerangka aplikasi: sidebar, topbar, dan satu area untuk layar aktif (UI/UX Spec §2).</summary>
public partial class MainWindow : Window
{
    public const string Dashboard = "dashboard";
    public const string Components = "components";
    public const string Profiles = "profiles";
    public const string DevConsole = "devconsole";
    public const string History = "history";

    public static readonly (string Key, string Title, string Icon)[] Menus =
    [
        (Dashboard, "Dashboard", "▦"),
        (Components, "Components", "⚙"),
        (Profiles, "Profiles", "◈"),
        (DevConsole, "Developer Console", "⌘"),
        (History, "History", "◷"),
    ];

    private const double CollapseBelowWidth = 900;   // mis. layar 1024 px pada skala DPI 125%
    private const double SidebarWidth = 184;
    private const double SidebarCollapsedWidth = 60;

    private readonly Func<string, FrameworkElement> _createScreen;
    private readonly Dictionary<string, FrameworkElement> _screens = [];
    private readonly Dictionary<string, RadioButton> _navButtons = [];
    private string _current = "";
    private bool _collapsed;

    /// <param name="createScreen">Membuat layar untuk satu menu (dipanggil sekali per menu, hasilnya disimpan).</param>
    public MainWindow(Func<string, FrameworkElement> createScreen)
    {
        InitializeComponent();
        _createScreen = createScreen;
        VersionText.Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");
        BuildMenu();
        Navigate(Dashboard);
    }

    public string CurrentKey => _current;
    public bool IsSidebarCollapsed => _collapsed;
    public bool IsMenuEnabled(string key) => _navButtons[key].IsEnabled;
    public FrameworkElement CurrentScreen => (FrameworkElement)ScreenHost.Content;

    private void BuildMenu()
    {
        foreach (var (key, title, icon) in Menus)
        {
            var label = new TextBlock { Text = title, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Name = "Label" };
            var iconText = new TextBlock { Text = icon, Width = 18, TextAlignment = TextAlignment.Center, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            var button = new RadioButton
            {
                GroupName = "Menu",
                Style = (Style)FindResource("NavButton"),
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { iconText, label } },
                ToolTip = title,
                Tag = key,
            };
            // Navigate sendiri mencentang tombol; guard ini mencegah layar dibuka dan di-refresh dua kali.
            button.Checked += (_, _) => { if (_current != key) Navigate(key); };
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            _navButtons[key] = button;
            Nav.Children.Add(button);
        }
    }

    /// <summary>Menampilkan layar menu; layar dibuat saat pertama dibuka lalu dipakai ulang dan di-refresh.</summary>
    public void Navigate(string key)
    {
        var (_, title, _) = Menus.Single(m => m.Key == key);
        if (!_screens.TryGetValue(key, out var screen))
        {
            screen = _createScreen(key);
            if (screen is IScreen notifying)
                notifying.Message += ShowToast;
            _screens[key] = screen;
        }

        _current = key;
        ScreenHost.Content = screen;
        CrumbText.Text = title;
        _navButtons[key].IsChecked = true;
        ContentScroll.ScrollToTop();
        (screen as IScreen)?.Refresh();
    }

    /// <summary>
    /// Menampilkan status mesin di topbar dan mengunci menu yang hanya untuk Development (D6/D7).
    /// config null = machine.ini tidak bisa dibaca → titik merah dan Developer Console terkunci.
    /// </summary>
    public void SetMachine(MachineConfig? config, IReadOnlyList<string> errors)
    {
        if (config is null)
        {
            MachineText.Text = "machine.ini is not valid";
            StatusDot.Fill = (Brush)FindResource("Brush.Red");
            MachineText.ToolTip = string.Join(Environment.NewLine, errors);
        }
        else
        {
            MachineText.Text = $"{config.MachineName} · {config.Environment}";
            StatusDot.Fill = (Brush)FindResource("Brush.Online");
            MachineText.ToolTip = null;
        }

        bool allowDevConsole = config?.IsDevelopment == true;
        _navButtons[DevConsole].IsEnabled = allowDevConsole;
        _navButtons[DevConsole].ToolTip = allowDevConsole ? "Developer Console" : "Only available on Development machines";

        // Layar yang sedang tampil (mis. Developer Console) perlu tahu bahwa mesin berubah/terkunci.
        if (!allowDevConsole && _current == DevConsole)
            Navigate(Dashboard);
    }

    public void ShowToast(string message) => ToastBox.Show(message);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize.Width);

    /// <summary>Sidebar mengecil menjadi ikon saja bila jendela sempit (UI/UX Spec §2).</summary>
    private void ApplyLayout(double width)
    {
        bool collapse = width < CollapseBelowWidth;
        if (collapse == _collapsed)
            return;

        _collapsed = collapse;
        SidebarColumn.Width = new GridLength(collapse ? SidebarCollapsedWidth : SidebarWidth);
        BrandText.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        VersionText.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in _navButtons.Values)
            ((StackPanel)button.Content).Children[1].Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
    }
}
