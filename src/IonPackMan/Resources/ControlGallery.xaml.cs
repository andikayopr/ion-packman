using System.Windows.Controls;
using IonPackMan.Controls;

namespace IonPackMan.Resources;

public partial class ControlGallery : UserControl
{
    public sealed record ServiceRow(string Service, string Component, string Status, StatusKind Kind, string Startup);

    public ControlGallery()
    {
        InitializeComponent();
        Services.ItemsSource = new[]
        {
            new ServiceRow("IonMiddleware", "ion-middleware", "Running", StatusKind.Success, "Automatic"),
            new ServiceRow("MddService", "mdd-service", "Stopped", StatusKind.Neutral, "Manual"),
            new ServiceRow("WIND", "wind", "Running", StatusKind.Success, "Automatic"),
        };
    }
}
