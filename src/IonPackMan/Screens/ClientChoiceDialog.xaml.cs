using System.Windows;
using IonPackMan.Machine;

namespace IonPackMan.Screens;

/// <summary>
/// Memilih asal build yang akan diimpor: Default, atau salah satu Client yang meng-override component (ADR-006).
/// Pilihan awal adalah saran dari branch git, tetapi developer tetap mengonfirmasi.
/// </summary>
public partial class ClientChoiceDialog : Window
{
    public sealed record Choice(string Id, string Text)
    {
        public override string ToString() => Text;   // kotak pilihan ComboBox menampilkan ToString()
    }

    public ClientChoiceDialog(string componentLabel, string branch, IReadOnlyList<string> clients, ClientConfig config, string? suggested, string machineClient = "")
    {
        InitializeComponent();
        HeaderText.Text = $"Which branch is this {componentLabel} build from?";
        string mapping = $"{string.Join(", ", clients)} {(clients.Count == 1 ? "has its own branch" : "have their own branches")} of this component. "
                         + "A machine with that Client deploys only its own build; every other machine deploys the Default build.";
        HintText.Text = (branch.Length > 0 ? $"The folder is on git branch '{branch}'. " : "")
                        + mapping
                        + (machineClient.Length > 0 ? $" This machine's Client is {machineClient}." : "");

        var choices = new List<Choice> { new("", "Default (all Clients)") };
        choices.AddRange(clients.Select(c => new Choice(c, config.Clients.TryGetValue(c, out var name) && name.Length > 0 ? $"{c} · {name}" : c)));
        ClientBox.ItemsSource = choices;
        ClientBox.SelectedValue = suggested ?? "";
    }

    /// <summary>Client terpilih; "" = Default.</summary>
    public string SelectedClient { get; private set; } = "";

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        SelectedClient = ClientBox.SelectedValue as string ?? "";
        DialogResult = true;
    }
}
