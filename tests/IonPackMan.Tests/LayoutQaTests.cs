using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>
/// T19: checklist tata letak UI/UX Spec §9 yang bisa diperiksa otomatis — tidak ada scroll horizontal,
/// sidebar/topbar tetap, tiap layar muat di 1024×768 dan di jendela sempit (sidebar ciut).
/// PNG tiap layar ditulis ke folder temp untuk diperiksa mata.
/// </summary>
public class LayoutQaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));

    public LayoutQaTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MachineIni()
    {
        string path = Path.Combine(_dir, "machine.ini");
        File.WriteAllText(path, $"[Meta]\nSchemaVersion=1.0\n[Machine]\nMachineName=M1\nEnvironment=Development\n[InstallLocations]\nION=D:\\ION\n[PackMan]\nLocalCacheDirectory={_dir}\\Cache\n");
        return path;
    }

    [Theory]
    [InlineData(1024, 768)]   // target: layar ATM, skala 100%
    [InlineData(820, 560)]    // 1024×700 pada skala 125% → sidebar ciut
    public void EveryScreen_FitsWithoutHorizontalScroll(int width, int height)
    {
        WpfHost.Run(() =>
        {
            var services = AppServices.Create(AppContext.BaseDirectory, MachineIni());
            MainWindow window = null!;
            window = new MainWindow(key => IonPackMan.Screens.ScreenFactory.Create(key, services, t => window.ShowToast(t)));
            window.SetMachine(services.Machine, services.MachineErrors);

            // Ukuran diatur sebelum menu dibuka; RenderWindowToPng menampilkan jendela dan mengukur ulang.
            foreach (var (key, title, _) in MainWindow.Menus)
            {
                window.Navigate(key);
                string png = Path.Combine(Path.GetTempPath(), $"ionpackman-qa-{width}-{key}.png");
                WpfHost.RenderWindowToPng(window, width, height, png);

                var scroll = (ScrollViewer)window.FindName("ContentScroll");
                var screen = window.CurrentScreen;
                Assert.True(scroll.ScrollableWidth < 0.5, $"{title}: horizontal scroll {scroll.ScrollableWidth}px at {width}px");
                Assert.True(screen.ActualWidth <= scroll.ViewportWidth + 0.5, $"{title}: screen {screen.ActualWidth}px wider than viewport {scroll.ViewportWidth}px");
                Assert.Equal(width < 900, window.IsSidebarCollapsed);
            }
            window.Close();
        });
    }
}
