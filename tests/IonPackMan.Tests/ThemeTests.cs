using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IonPackMan.Controls;
using IonPackMan.Resources;
using Xunit;

namespace IonPackMan.Tests;

public class ThemeTests
{
    // Area konten di 1024×768: 1024 − sidebar 184 − padding 2×16 ≈ 808 (UI/UX Spec §1).
    private const double ContentWidth = 808 + 32;

    private static Color Brush(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    // Style implisit (dari Application.Resources) hanya berlaku bila elemen berada di dalam sebuah Window.
    // Window tidak perlu ditampilkan.
    private static T Realize<T>(T element) where T : FrameworkElement
    {
        _ = new Window { Content = element };
        element.Measure(new Size(400, 400));
        return element;
    }

    [Fact]
    public void Gallery_Renders_FitsContentWidth_AndWritesPreviewPng()
    {
        string png = Path.Combine(Path.GetTempPath(), "ionpackman-gallery.png");

        var (width, height) = WpfHost.Run(() => WpfHost.RenderToPng(new ControlGallery(), ContentWidth, png));

        Assert.True(File.Exists(png) && new FileInfo(png).Length > 5_000, "preview PNG was not written");
        Assert.True(width <= ContentWidth + 0.5, $"gallery is {width}px wide, wider than the {ContentWidth}px content area");
        Assert.True(height > 400);
    }

    [Fact]
    public void StatusPill_UsesTheRightColourForEachKind()
    {
        WpfHost.Run(() =>
        {
            (StatusKind Kind, string Bg, string Fg)[] expected =
            [
                (StatusKind.Success, "Brush.Success.Bg", "Brush.Success.Fg"),
                (StatusKind.Warning, "Brush.Warning.Bg", "Brush.Warning.Fg"),
                (StatusKind.Error, "Brush.Error.Bg", "Brush.Error.Fg"),
                (StatusKind.Info, "Brush.Info.Bg", "Brush.Info.Fg"),
                (StatusKind.Neutral, "Brush.Neutral.Bg", "Brush.Neutral.Fg"),
            ];

            foreach (var (kind, bg, fg) in expected)
            {
                var pill = Realize(new StatusPill { Text = kind.ToString(), Kind = kind });
                Assert.Equal(Brush(bg), ((SolidColorBrush)pill.Background).Color);
                Assert.Equal(Brush(fg), ((SolidColorBrush)pill.Foreground).Color);

                var text = WpfHost.Descendants<TextBlock>(pill).Single();
                Assert.Equal(kind.ToString(), text.Text);   // status selalu berupa teks, bukan hanya warna
            }
        });
    }

    [Fact]
    public void Notice_DefaultsToWarning_AndSwitchesColourByKind()
    {
        WpfHost.Run(() =>
        {
            Assert.Equal(Brush("Brush.Warning.Bg"), ((SolidColorBrush)Realize(new Notice()).Background).Color);
            Assert.Equal(Brush("Brush.Error.Bg"), ((SolidColorBrush)Realize(new Notice { Kind = StatusKind.Error }).Background).Color);
            Assert.Equal(Brush("Brush.Success.Border"), ((SolidColorBrush)Realize(new Notice { Kind = StatusKind.Success }).BorderBrush).Color);
        });
    }

    [Fact]
    public void Buttons_PrimaryIsBlue_DisabledIsFaded()
    {
        WpfHost.Run(() =>
        {
            var primary = new Button { Content = "Execute", Style = (Style)Application.Current.FindResource("PrimaryButton") };
            var plain = new Button { Content = "Cancel" };
            var disabled = new Button { Content = "Execute", IsEnabled = false };
            foreach (var b in new[] { primary, plain, disabled }) Realize(b);

            Assert.Equal(Brush("Brush.Blue"), ((SolidColorBrush)primary.Background).Color);
            Assert.Equal(Colors.White, ((SolidColorBrush)primary.Foreground).Color);
            Assert.Equal(Brush("Brush.Panel"), ((SolidColorBrush)plain.Background).Color);
            Assert.Equal(32, primary.Height);          // tinggi kontrol (UI/UX Spec §3)
            Assert.Equal(0.45, disabled.Opacity, 2);
        });
    }

    [Fact]
    public void Toast_StartsHidden_ShowsMessage_AndHides()
    {
        WpfHost.Run(() =>
        {
            var toast = new Toast();
            Assert.Equal(Visibility.Collapsed, toast.Visibility);

            toast.Show("Component release saved.");
            Assert.Equal(Visibility.Visible, toast.Visibility);
            Assert.Equal("Component release saved.", toast.Message);

            toast.Hide();
            Assert.Equal(Visibility.Collapsed, toast.Visibility);
        });
    }

    [Fact]
    public void Card_HidesItsTitleWhenThereIsNoHeader()
    {
        WpfHost.Run(() =>
        {
            var style = (Style)Application.Current.FindResource("Card");
            var withTitle = new HeaderedContentControl { Style = style, Header = "Title", Content = new TextBlock() };
            var without = new HeaderedContentControl { Style = style, Content = new TextBlock() };
            foreach (var c in new[] { withTitle, without }) { c.ApplyTemplate(); c.Measure(new Size(300, 300)); }

            Assert.Equal(Visibility.Visible, ((FrameworkElement)withTitle.Template.FindName("Title", withTitle)).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)without.Template.FindName("Title", without)).Visibility);
        });
    }
}
