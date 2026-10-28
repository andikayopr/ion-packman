using System.Windows;
using IonPackMan.Machine;
using IonPackMan.Screens;

namespace IonPackMan;

public partial class App : Application
{
    // Hanya satu instance PackMan per mesin (PRD R11). "Global\" agar berlaku lintas sesi Windows.
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, Paths.SingleInstanceMutexName, out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("ION PackMan is already running on this machine.",
                "ION PackMan", MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var (machine, errors) = MachineConfig.Load(Paths.MachineIni);
        var window = new MainWindow(CreateScreen);
        window.SetMachine(machine, errors);
        window.Show();
    }

    // Layar sungguhan didaftarkan di sini satu per satu seiring tiketnya selesai.
    private static FrameworkElement CreateScreen(string key)
    {
        // Di dalam App, "MainWindow" berarti properti Application.MainWindow, jadi tulis lengkap.
        var (_, title, icon) = IonPackMan.MainWindow.Menus.Single(m => m.Key == key);
        return new PlaceholderScreen(title, "This screen is not built yet.", icon);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
