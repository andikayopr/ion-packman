using System.Windows;
using System.Windows.Controls;
using IonPackMan.Controls;

namespace IonPackMan.Screens;

/// <summary>Layar sementara untuk menu yang belum dikerjakan atau sedang dikunci.</summary>
public sealed class PlaceholderScreen : UserControl, IScreen
{
    // Layar ini tidak pernah mengirim pesan.
    event Action<string>? IScreen.Message { add { } remove { } }

    public PlaceholderScreen(string title, string message, string icon = "◷")
    {
        var heading = new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("PageTitle") };
        var card = new HeaderedContentControl
        {
            Style = (Style)Application.Current.FindResource("Card"),
            Margin = new Thickness(0, 16, 0, 0),
            Content = new EmptyState { Icon = icon, Title = title, Message = message },
        };
        Content = new StackPanel { Children = { heading, card } };
    }

    public void Refresh() { }
}
