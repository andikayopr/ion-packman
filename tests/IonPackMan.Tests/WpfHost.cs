using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IonPackMan;

namespace IonPackMan.Tests;

/// <summary>
/// WPF hanya boleh dipakai dari satu thread STA dan hanya boleh ada satu Application per proses.
/// Semua test UI dijalankan lewat thread khusus ini; Application dibuat dari App.xaml asli (tema ikut termuat).
/// </summary>
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> Ui = new(Start);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "WPF test thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    public static T Run<T>(Func<T> work) => Ui.Value.Invoke(work);
    public static void Run(Action work) => Ui.Value.Invoke(work);

    /// <summary>Menampilkan elemen di jendela di luar layar pada lebar tertentu, lalu menyimpannya sebagai PNG.</summary>
    public static (double Width, double Height) RenderToPng(FrameworkElement content, double width, string pngPath)
    {
        var root = new System.Windows.Controls.Border { Background = (Brush)Application.Current.FindResource("Brush.Bg"), Child = content };
        var window = new Window
        {
            Content = root, Width = width, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000,
        };
        window.Show();
        window.UpdateLayout();
        // DataGrid membuat kolom dan sel secara bertahap; beri dispatcher satu putaran lalu hitung ulang tata letak.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();

        double height = root.ActualHeight;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(pngPath))
            encoder.Save(file);

        root.Child = null;
        window.Close();
        return (root.ActualWidth, height);
    }

    /// <summary>Menampilkan Window apa adanya (di luar layar) pada ukuran tertentu dan menyimpannya sebagai PNG.</summary>
    public static void RenderWindowToPng(Window window, double width, double height, string pngPath)
    {
        window.WindowState = WindowState.Normal;
        window.WindowStyle = WindowStyle.None;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Left = -20000;
        window.Top = -20000;
        window.Width = width;
        window.Height = height;
        window.Show();
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();

        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(pngPath);
        encoder.Save(file);
    }

    public static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }
}
