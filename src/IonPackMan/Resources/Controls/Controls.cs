using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace IonPackMan.Controls;

/// <summary>Warna makna untuk StatusPill dan Notice (UI/UX Spec §3). Status selalu disertai teks, bukan hanya warna.</summary>
public enum StatusKind { Neutral, Success, Warning, Error, Info }

/// <summary>Label status bulat kecil, mis. "Running" (hijau) atau "Stopped" (abu). Tampilan ada di Theme.xaml.</summary>
public class StatusPill : Control
{
    static StatusPill() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusPill), new FrameworkPropertyMetadata(typeof(StatusPill)));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata(""));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(StatusKind), typeof(StatusPill), new PropertyMetadata(StatusKind.Neutral));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public StatusKind Kind { get => (StatusKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
}

/// <summary>Kotak pesan berwarna (peringatan kuning, sukses hijau, error merah, info biru). Isinya bebas.</summary>
public class Notice : ContentControl
{
    static Notice() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Notice), new FrameworkPropertyMetadata(typeof(Notice)));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(StatusKind), typeof(Notice), new PropertyMetadata(StatusKind.Warning));

    public StatusKind Kind { get => (StatusKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
}

/// <summary>Tampilan untuk tabel/daftar kosong: ikon besar, judul, dan satu kalimat penjelasan.</summary>
public class EmptyState : Control
{
    static EmptyState() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(typeof(EmptyState)));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(EmptyState), new PropertyMetadata("◷"));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
}

/// <summary>Pesan singkat di pojok kanan bawah yang hilang sendiri setelah 3 detik. Taruh satu di MainWindow.</summary>
public class Toast : Control
{
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    static Toast() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Toast), new FrameworkPropertyMetadata(typeof(Toast)));

    public Toast()
    {
        Visibility = Visibility.Collapsed;
        _hideTimer.Tick += (_, _) => Hide();
    }

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(Toast), new PropertyMetadata(""));

    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

    public void Show(string message)
    {
        Message = message;
        Visibility = Visibility.Visible;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void Hide()
    {
        _hideTimer.Stop();
        Visibility = Visibility.Collapsed;
    }
}
