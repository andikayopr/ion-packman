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

        var (options, argumentError) = StartupOptions.Parse(e.Args);
        if (options is null)
        {
            MessageBox.Show(argumentError, "ION PackMan", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        var services = AppServices.Create(options.ReleaseDirectory, options.MachineIni);
        // 'window' dipakai toast dan Open; keduanya hanya dipanggil setelah konstruktor selesai. Selalu lewat lambda:
        // method group (window.Open) akan dievaluasi sekarang, saat window masih null, dan membuat aplikasi crash.
        MainWindow window = null!;
        window = new MainWindow(key => ScreenFactory.Create(key, services, text => window.ShowToast(text), k => window.Open(k)),
            hasPendingRecovery: () => services.Recovery?.HasPending() == true);
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
