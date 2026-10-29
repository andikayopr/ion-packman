using System.Windows;
using System.Windows.Input;
using IonPackMan.Builds;
using IonPackMan.Components;
using IonPackMan.Machine;
using Microsoft.Win32;

namespace IonPackMan.Screens;

/// <summary>
/// Alur "Import local build…" dari layar Components: pilih folder, pilih asal build (Default atau Client) bila component
/// punya branch Client, konfirmasi bila menggantikan build lama, lalu jalankan <see cref="BuildImporter"/>.
/// Hanya dialog dan pesan; aturan impor ada di BuildImporter.
/// </summary>
public static class BuildImportFlow
{
    /// <returns>true bila build berhasil diimpor.</returns>
    public static bool Run(BuildImporter importer, ComponentRelease release, ClientConfig clients, Action<string> notify, string machineClient = "")
    {
        var dialog = new OpenFolderDialog { Title = $"Select the build folder for {release.Id} {release.ReleaseVersion}" };
        if (dialog.ShowDialog() != true)
            return false;

        // Component yang tidak punya branch Client selalu default: tidak ada yang perlu ditanyakan.
        string client = "";
        var candidates = clients.ClientsOverriding(release.Id);
        if (candidates.Count > 0)
        {
            var (branch, _) = GitInfo.Read(dialog.FolderName);
            var choice = new ClientChoiceDialog($"{release.Id} {release.ReleaseVersion}", branch, candidates, clients, clients.Suggest(branch, release.Id)
                ?? (clients.Overrides(machineClient, release.Id) ? clients.ExactId(machineClient) : null), machineClient)
            {
                Owner = Application.Current?.MainWindow,
            };
            if (choice.ShowDialog() != true)
                return false;
            client = choice.SelectedClient;
        }

        string label = BuildImporter.Label(release.Id, release.ReleaseVersion, client);
        if (importer.Exists(release.Id, release.ReleaseVersion, client))
        {
            var answer = MessageBox.Show(
                $"A build for {label} is already imported.\nReplace it with the files from:\n{dialog.FolderName}?",
                "Replace imported build", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK)
                return false;
        }

        var previous = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        (BuildInfo? Build, List<string> Errors) result;
        try
        {
            result = importer.Import(release.Id, release.ReleaseVersion, dialog.FolderName, client: client);
        }
        finally
        {
            Mouse.OverrideCursor = previous;
        }

        if (result.Build is null)
        {
            MessageBox.Show(string.Join(Environment.NewLine, result.Errors), "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        notify($"Imported {result.Build.FileCount} files for {label}.");
        return true;
    }
}
