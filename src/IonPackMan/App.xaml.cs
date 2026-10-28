using System.Windows;
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

        var services = AppServices.Create(Paths.ReleaseDirectory, Paths.MachineIni);
        MainWindow window = null!;   // dipakai toast; baru terisi setelah konstruktor, sebelum ada yang menekan tombol
        window = new MainWindow(key => ScreenFactory.Create(key, services, text => window.ShowToast(text)));
        window.SetMachine(services.Machine, services.MachineErrors);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
